using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TiToAI.Contracts.Jobs;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Persistence.Entities;

namespace TiToAI.Application.Jobs;

/// <summary>
/// Dịch vụ nghiệp vụ quản lý vòng đời Hàng đợi Tác vụ Bền vững (Job Queue)
/// theo kiến trúc T15 và hợp đồng JobDtos.
/// </summary>
public sealed class JobService
{
    private readonly DbContextOptions<TiToAIDbContext> _dbOptions;
    private readonly JobSnapshotFactory _snapshotFactory;
    private readonly JobScheduler _scheduler;
    private readonly IJobWakeupNotifier _wakeupNotifier;
    private readonly Func<DateTimeOffset>? _clock;

    public JobService(
        DbContextOptions<TiToAIDbContext> dbOptions,
        JobSnapshotFactory snapshotFactory,
        JobScheduler scheduler,
        IJobWakeupNotifier? wakeupNotifier = null,
        Func<DateTimeOffset>? clock = null)
    {
        _dbOptions = dbOptions ?? throw new ArgumentNullException(nameof(dbOptions));
        _snapshotFactory = snapshotFactory ?? throw new ArgumentNullException(nameof(snapshotFactory));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _wakeupNotifier = wakeupNotifier ?? scheduler;
        _clock = clock;
    }

    private DateTimeOffset UtcNow => _clock?.Invoke() ?? DateTimeOffset.UtcNow;

    /// <summary>
    /// Xếp hàng tác vụ mới (job.enqueue): Xác thực ranh giới, đóng băng snapshot bất biến và lưu vào SQLite.
    /// Kích hoạt tín hiệu đánh thức qua Channel để Scheduler phân phối ngay lập tức.
    /// </summary>
    public async Task<JobEnqueueResponseData> EnqueueAsync(
        JobEnqueueRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.WorkspaceId))
        {
            throw new JobValidationException("Trường 'workspaceId' không được để trống.", nameof(request.WorkspaceId));
        }

        if (string.IsNullOrWhiteSpace(request.ProjectId))
        {
            throw new JobValidationException("Trường 'projectId' không được để trống.", nameof(request.ProjectId));
        }

        if (!JobTaskType.IsValid(request.TaskType))
        {
            throw new JobValidationException($"Loại tác vụ '{request.TaskType}' không hợp lệ. Các loại cho phép: {string.Join(", ", JobTaskType.All)}.", nameof(request.TaskType));
        }

        await using var dbContext = new TiToAIDbContext(_dbOptions);

        // 1. Xác thực Không gian làm việc
        var workspaceExists = await dbContext.Workspaces
            .AsNoTracking()
            .AnyAsync(w => w.Id == request.WorkspaceId, cancellationToken).ConfigureAwait(false);

        if (!workspaceExists)
        {
            throw new JobValidationException($"Không tìm thấy Không gian làm việc với mã '{request.WorkspaceId}'.", nameof(request.WorkspaceId));
        }

        // 2. Xác thực Dự án
        var projectExists = await dbContext.Projects
            .AsNoTracking()
            .AnyAsync(p => p.Id == request.ProjectId && p.WorkspaceId == request.WorkspaceId, cancellationToken).ConfigureAwait(false);

        if (!projectExists)
        {
            throw new JobValidationException($"Không tìm thấy Dự án '{request.ProjectId}' thuộc Không gian làm việc '{request.WorkspaceId}'.", nameof(request.ProjectId));
        }

        // 3. Xác thực Phân cảnh (CK_Job_TaskType_Scene: Script => null, Image/Video* => bắt buộc)
        string? normalizedSceneId = null;
        if (request.TaskType == JobTaskType.Script)
        {
            normalizedSceneId = null;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.SceneId))
            {
                throw new JobValidationException($"Tác vụ '{request.TaskType}' bắt buộc phải thuộc một Phân cảnh cụ thể.", nameof(request.SceneId));
            }

            normalizedSceneId = request.SceneId.Trim();
            var sceneExists = await dbContext.Scenes
                .AsNoTracking()
                .AnyAsync(s => s.Id == normalizedSceneId && s.ProjectId == request.ProjectId && s.WorkspaceId == request.WorkspaceId, cancellationToken).ConfigureAwait(false);

            if (!sceneExists)
            {
                throw new JobValidationException($"Không tìm thấy Phân cảnh '{normalizedSceneId}' thuộc Dự án '{request.ProjectId}'.", nameof(request.SceneId));
            }
        }

        // 4. Chụp bản sao bất biến của các tham số đầu vào (Provider, Storage Root, Prompt, Config)
        var snapshot = await _snapshotFactory.CreateSnapshotAsync(request, cancellationToken).ConfigureAwait(false);

        var nowIso = UtcNow.ToString("o");
        var jobId = Guid.NewGuid().ToString();

        // 5. Khởi tạo thực thể Job bền vững trong SQLite
        var jobEntity = new Job
        {
            Id = jobId,
            WorkspaceId = request.WorkspaceId,
            ProjectId = request.ProjectId,
            SceneId = normalizedSceneId,
            TaskType = request.TaskType,
            State = JobState.Queued,
            ProviderKey = snapshot.ProviderKey,
            StorageRootId = snapshot.StorageRootId,
            PromptSnapshotText = snapshot.PromptSnapshotText,
            ConfigSnapshotJson = snapshot.ConfigSnapshotJson,
            Attempts = 0,
            MaxAttempts = 3,
            FencingToken = 0,
            SideEffectSubmitted = 0,
            CancelRequested = 0,
            CreatedAtUtc = nowIso,
            UpdatedAtUtc = nowIso
        };

        dbContext.Jobs.Add(jobEntity);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // 6. Gửi tín hiệu đánh thức tức thời tới Scheduler qua Channel
        _wakeupNotifier.NotifyWakeup(new JobWakeupSignal(
            Source: "job.enqueue",
            WorkspaceId: request.WorkspaceId,
            JobId: jobId
        ));

        return new JobEnqueueResponseData(
            JobId: jobId,
            State: JobState.Queued,
            WorkspaceId: request.WorkspaceId,
            TaskType: request.TaskType,
            ProviderKey: snapshot.ProviderKey,
            StorageRootId: snapshot.StorageRootId,
            CreatedAtUtc: nowIso
        );
    }

    /// <summary>
    /// Lấy chi tiết trạng thái của một tác vụ (job.get_status).
    /// </summary>
    public async Task<JobGetStatusResponseData> GetStatusAsync(
        string workspaceId,
        string jobId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        await using var dbContext = new TiToAIDbContext(_dbOptions);

        var job = await dbContext.Jobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == jobId && j.WorkspaceId == workspaceId, cancellationToken).ConfigureAwait(false);

        if (job is null)
        {
            throw new JobNotFoundException(jobId);
        }

        var detail = new JobDetailDto(
            Id: job.Id,
            WorkspaceId: job.WorkspaceId,
            ProjectId: job.ProjectId,
            SceneId: job.SceneId,
            TaskType: job.TaskType,
            State: job.State,
            ReasonCode: job.ReasonCode,
            ProviderKey: job.ProviderKey,
            StorageRootId: job.StorageRootId,
            Attempts: job.Attempts,
            MaxAttempts: job.MaxAttempts,
            SideEffectSubmitted: job.SideEffectSubmitted,
            ResultReference: job.ResultReference,
            ResultText: job.ResultText,
            CancelRequested: job.CancelRequested == 1,
            CreatedAtUtc: job.CreatedAtUtc,
            UpdatedAtUtc: job.UpdatedAtUtc
        );

        return new JobGetStatusResponseData(detail);
    }

    /// <summary>
    /// Liệt kê các tác vụ đang hoạt động (job.list_active): Queued, Reserved, Running, AwaitingUser.
    /// </summary>
    public async Task<JobListActiveResponseData> ListActiveAsync(
        string? workspaceId = null,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = new TiToAIDbContext(_dbOptions);

        var query = dbContext.Jobs
            .AsNoTracking()
            .Where(j => JobState.ActiveStates.Contains(j.State));

        if (!string.IsNullOrWhiteSpace(workspaceId))
        {
            query = query.Where(j => j.WorkspaceId == workspaceId);
        }

        var activeJobs = await query
            .OrderBy(j => j.CreatedAtUtc)
            .Select(j => new JobSummaryDto(
                j.Id,
                j.WorkspaceId,
                j.ProjectId,
                j.SceneId,
                j.TaskType,
                j.State,
                j.ReasonCode,
                j.Attempts,
                null, // ProgressPercent tính toán động nếu cần
                null,
                j.CreatedAtUtc
            ))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return new JobListActiveResponseData(activeJobs);
    }

    /// <summary>
    /// Hủy bỏ tác vụ (job.cancel):
    /// - Nếu đang Queued: Chuyển ngay sang Canceled.
    /// - Nếu đang Reserved/Running và chưa submit side-effect: Chuyển ngay sang Canceled.
    /// - Nếu đã submit side-effect: Đánh dấu cancel_requested = 1 để đối soát/worker xử lý an toàn.
    /// - Nếu đang AwaitingUser: Chuyển ngay sang Canceled.
    /// </summary>
    public async Task<JobCancelResponseData> CancelAsync(
        string workspaceId,
        string jobId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        await using var dbContext = new TiToAIDbContext(_dbOptions);

        var job = await dbContext.Jobs
            .FirstOrDefaultAsync(j => j.Id == jobId && j.WorkspaceId == workspaceId, cancellationToken).ConfigureAwait(false);

        if (job is null)
        {
            throw new JobNotFoundException(jobId);
        }

        if (JobState.IsTerminal(job.State))
        {
            throw new JobConcurrencyConflictException($"Tác vụ đã kết thúc ở trạng thái '{job.State}', không thể hủy bỏ.", jobId);
        }

        var prevState = job.State;
        var nowIso = UtcNow.ToString("o");

        if (job.State == JobState.Queued || (job.State is JobState.Reserved or JobState.Running && job.SideEffectSubmitted == 0))
        {
            job.State = JobState.Canceled;
            job.ReasonCode = JobReasonCode.UserCanceled;
            job.LeaseExpiresAtUtc = null;
            job.LeaseOwnerId = null;
            job.UpdatedAtUtc = nowIso;

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new JobCancelResponseData(
                JobId: jobId,
                State: JobState.Canceled,
                CancelRequested: false,
                Message: "Tác vụ đã được hủy bỏ thành công trước khi gửi side-effect.",
                PreviousState: prevState
            );
        }

        if (job.State == JobState.AwaitingUser)
        {
            job.State = JobState.Canceled;
            job.ReasonCode = JobReasonCode.UserCanceled;
            job.LeaseExpiresAtUtc = null;
            job.UpdatedAtUtc = nowIso;

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new JobCancelResponseData(
                JobId: jobId,
                State: JobState.Canceled,
                CancelRequested: false,
                Message: "Tác vụ đang chờ đối soát đã được người dùng hủy bỏ.",
                PreviousState: prevState
            );
        }

        // Job đang Running và đã submit side effect
        job.CancelRequested = 1;
        job.UpdatedAtUtc = nowIso;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new JobCancelResponseData(
            JobId: jobId,
            State: job.State,
            CancelRequested: true,
            Message: "Đã ghi nhận yêu cầu hủy tác vụ. Do tác dụng phụ đã gửi ra ngoài, hệ thống sẽ bảo vệ kết quả hoặc chuyển sang đối soát.",
            PreviousState: prevState
        );
    }

    /// <summary>
    /// Đối soát tác vụ bị treo hoặc yêu cầu người dùng can thiệp (job.reconcile):
    /// Áp dụng cho các tác vụ ở trạng thái AwaitingUser.
    /// </summary>
    public async Task<JobReconcileResponseData> ReconcileAsync(
        JobReconcileRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.JobId))
        {
            throw new JobValidationException("Trường 'jobId' không được để trống.", nameof(request.JobId));
        }

        await using var dbContext = new TiToAIDbContext(_dbOptions);

        var job = await dbContext.Jobs
            .FirstOrDefaultAsync(j => j.Id == request.JobId && j.WorkspaceId == request.WorkspaceId, cancellationToken).ConfigureAwait(false);

        if (job is null)
        {
            throw new JobNotFoundException(request.JobId);
        }

        if (job.State != JobState.AwaitingUser)
        {
            throw new JobConcurrencyConflictException(
                $"Tác vụ hiện ở trạng thái '{job.State}', không thể thực hiện đối soát (yêu cầu trạng thái 'AwaitingUser').",
                request.JobId);
        }

        var prevState = job.State;
        var nowIso = UtcNow.ToString("o");
        string currentState;
        string reconcileResult;

        switch (request.Action?.ToLowerInvariant())
        {
            case "retry":
            case "resubmit":
                // Chặn retry/resubmit nếu đã phát sinh side-effect mà chưa có bằng chứng provider chưa nhận
                if (job.SideEffectSubmitted == 1 && request.ConfirmedNotReceived != true)
                {
                    throw new JobValidationException(
                        "Không thể gửi lại tác vụ đã phát sinh tác dụng phụ (side-effect) ra ngoài khi chưa có bằng chứng xác nhận chắc chắn nhà cung cấp chưa tiếp nhận. Vui lòng đối soát hoặc nhập kết quả thủ công.",
                        nameof(request.Action));
                }

                if (job.Attempts >= job.MaxAttempts)
                {
                    throw new JobValidationException(
                        $"Tác vụ đã vượt quá số lần thử tối đa ({job.Attempts}/{job.MaxAttempts}). Vui lòng tạo tác vụ mới.",
                        nameof(job.Attempts));
                }

                // Chỉ khi có bằng chứng chắc chắn hoặc chưa gửi side effect mới được xóa cờ và xếp hàng lại
                job.State = JobState.Queued;
                job.SideEffectSubmitted = 0;
                job.SideEffectAtUtc = null;
                job.ReasonCode = null;
                job.LeaseExpiresAtUtc = null;
                job.LeaseOwnerId = null;
                job.UpdatedAtUtc = nowIso;
                currentState = JobState.Queued;
                reconcileResult = "Tác vụ đã được xác minh an toàn và đưa về hàng đợi để gửi lại.";
                _wakeupNotifier.NotifyWakeup(new JobWakeupSignal("job.reconcile.retry", job.WorkspaceId, job.Id));
                break;

            case "mark_succeeded":
            case "resolve":
                // mark_succeeded bắt buộc phải có kết quả hợp lệ (ResultReference hoặc ResultText), không chỉ đổi trạng thái
                var validReference = !string.IsNullOrWhiteSpace(request.ResultReference) ? request.ResultReference : job.ResultReference;
                var validText = !string.IsNullOrWhiteSpace(request.ResultText) ? request.ResultText : job.ResultText;

                if (string.IsNullOrWhiteSpace(validReference) && string.IsNullOrWhiteSpace(validText))
                {
                    throw new JobValidationException(
                        "Không thể đánh dấu tác vụ thành công (mark_succeeded) khi chưa có kết quả hợp lệ (ResultReference hoặc ResultText). Vui lòng cung cấp kết quả hoặc nhập thủ công.",
                        nameof(request.Action));
                }

                if (!string.IsNullOrWhiteSpace(request.ResultReference))
                {
                    job.ResultReference = request.ResultReference;
                }
                if (!string.IsNullOrWhiteSpace(request.ResultText))
                {
                    job.ResultText = request.ResultText;
                }

                job.State = JobState.Succeeded;
                job.ReasonCode = null;
                job.LeaseExpiresAtUtc = null;
                job.LeaseOwnerId = null;
                job.UpdatedAtUtc = nowIso;
                currentState = JobState.Succeeded;
                reconcileResult = "Tác vụ đã được xác nhận hoàn tất thành công với kết quả hợp lệ.";
                break;

            case "mark_failed":
                job.State = JobState.Failed;
                job.ReasonCode = "reconcile_rejected";
                job.LeaseExpiresAtUtc = null;
                job.LeaseOwnerId = null;
                job.UpdatedAtUtc = nowIso;
                currentState = JobState.Failed;
                reconcileResult = "Tác vụ đã được đánh dấu thất bại sau đối soát.";
                break;

            case "cancel":
                job.State = JobState.Canceled;
                job.ReasonCode = JobReasonCode.UserCanceled;
                job.LeaseExpiresAtUtc = null;
                job.LeaseOwnerId = null;
                job.UpdatedAtUtc = nowIso;
                currentState = JobState.Canceled;
                reconcileResult = "Tác vụ đã được hủy bỏ bởi người dùng.";
                break;

            default:
                throw new JobValidationException(
                    $"Hành động đối soát '{request.Action}' không hợp lệ. Các hành động hỗ trợ: retry, mark_succeeded, mark_failed, cancel.",
                    nameof(request.Action));
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new JobReconcileResponseData(
            JobId: job.Id,
            PreviousState: prevState,
            CurrentState: currentState,
            ReconcileResult: reconcileResult,
            ResultReference: job.ResultReference
        );
    }

    /// <summary>
    /// Chạy quét phục hồi sự cố khởi động (3-tier startup recovery sweep).
    /// </summary>
    public Task<int> RunCrashRecoverySweepAsync(CancellationToken cancellationToken = default)
    {
        return _scheduler.RunCrashRecoverySweepAsync(cancellationToken);
    }
}
