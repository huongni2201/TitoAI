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
    private readonly JobReconciliationService _reconciliationService;
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
        _reconciliationService = new JobReconciliationService(_dbOptions, _wakeupNotifier, clock);
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

        var binding = await dbContext.ExecutionBindings
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.JobId == jobId, cancellationToken).ConfigureAwait(false);

        bool hasOccupancy = await dbContext.ActiveSlotOccupancies
            .AsNoTracking()
            .AnyAsync(o => o.JobId == jobId, cancellationToken).ConfigureAwait(false);

        bool hasUnresolved = hasOccupancy ||
            ((job.State == JobState.Canceled || job.State == JobState.Failed || job.State == JobState.AwaitingUser) &&
             job.SideEffectSubmitted == 1 &&
             string.IsNullOrWhiteSpace(job.ResultReference) &&
             string.IsNullOrWhiteSpace(job.ResultText));

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
            UpdatedAtUtc: job.UpdatedAtUtc,
            ProfileId: binding?.ProfileId,
            HasUnresolvedSideEffect: hasUnresolved
        );

        return new JobGetStatusResponseData(detail);
    }

    /// <summary>
    /// Liệt kê các tác vụ đang hoạt động (job.list_active): Queued, Reserved, Running, AwaitingUser
    /// và các tác vụ terminal (Canceled/Failed) còn unresolved (đang giữ ActiveSlotOccupancy hoặc side-effect in-flight chưa giải quyết).
    /// </summary>
    public async Task<JobListActiveResponseData> ListActiveAsync(
        string? workspaceId = null,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = new TiToAIDbContext(_dbOptions);

        var query = dbContext.Jobs
            .AsNoTracking()
            .Where(j => JobState.ActiveStates.Contains(j.State)
                || ((j.State == JobState.Canceled || j.State == JobState.Failed) &&
                    (dbContext.ActiveSlotOccupancies.Any(o => o.JobId == j.Id) ||
                     (j.SideEffectSubmitted == 1 && (j.ResultReference == null || j.ResultReference == "") && (j.ResultText == null || j.ResultText == "")))));

        if (!string.IsNullOrWhiteSpace(workspaceId))
        {
            query = query.Where(j => j.WorkspaceId == workspaceId);
        }

        var jobs = await query
            .OrderBy(j => j.CreatedAtUtc)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var jobIds = jobs.Select(j => j.Id).ToList();

        var bindings = await dbContext.ExecutionBindings
            .AsNoTracking()
            .Where(b => jobIds.Contains(b.JobId))
            .ToDictionaryAsync(b => b.JobId, b => b.ProfileId, cancellationToken).ConfigureAwait(false);

        var occupancies = await dbContext.ActiveSlotOccupancies
            .AsNoTracking()
            .Where(o => jobIds.Contains(o.JobId))
            .Select(o => o.JobId)
            .Distinct()
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var occupancySet = new HashSet<string>(occupancies);

        var activeJobs = jobs.Select(j =>
        {
            bindings.TryGetValue(j.Id, out var profileId);
            bool hasUnresolved = occupancySet.Contains(j.Id) ||
                ((j.State == JobState.Canceled || j.State == JobState.Failed || j.State == JobState.AwaitingUser) &&
                 j.SideEffectSubmitted == 1 &&
                 string.IsNullOrWhiteSpace(j.ResultReference) &&
                 string.IsNullOrWhiteSpace(j.ResultText));

            return new JobSummaryDto(
                Id: j.Id,
                WorkspaceId: j.WorkspaceId,
                ProjectId: j.ProjectId,
                SceneId: j.SceneId,
                TaskType: j.TaskType,
                State: j.State,
                ReasonCode: j.ReasonCode,
                Attempts: j.Attempts,
                ProgressPercent: null,
                CurrentStep: null,
                CreatedAtUtc: j.CreatedAtUtc,
                MaxAttempts: j.MaxAttempts,
                SideEffectSubmitted: j.SideEffectSubmitted,
                CancelRequested: j.CancelRequested == 1,
                HasUnresolvedSideEffect: hasUnresolved,
                ProfileId: profileId,
                ResultReference: j.ResultReference
            );
        }).ToList();

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
            job.CancelRequested = 1;
            job.ReasonCode = JobReasonCode.UserCanceled;
            job.LeaseExpiresAtUtc = null;
            job.LeaseOwnerId = null;
            job.UpdatedAtUtc = nowIso;

            // Giải phóng slot occupancy nếu đã được cấp
            var occupancy = await dbContext.ActiveSlotOccupancies
                .FirstOrDefaultAsync(o => o.JobId == jobId, cancellationToken).ConfigureAwait(false);
            if (occupancy != null)
            {
                dbContext.ActiveSlotOccupancies.Remove(occupancy);
            }

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
            job.CancelRequested = 1;
            job.ReasonCode = JobReasonCode.UserCanceled;
            job.LeaseExpiresAtUtc = null;
            job.UpdatedAtUtc = nowIso;

            // T16 FINDING 2: Nếu job đã submit side effect nhưng chưa có kết quả (unresolved),
            // CẤM giải phóng ActiveSlotOccupancy để bảo vệ capacity cho đến khi đối soát an toàn!
            bool hasUnresolvedSideEffect = job.SideEffectSubmitted == 1 &&
                                           string.IsNullOrWhiteSpace(job.ResultReference) &&
                                           string.IsNullOrWhiteSpace(job.ResultText);

            if (!hasUnresolvedSideEffect)
            {
                var occupancy = await dbContext.ActiveSlotOccupancies
                    .FirstOrDefaultAsync(o => o.JobId == jobId, cancellationToken).ConfigureAwait(false);
                if (occupancy != null)
                {
                    dbContext.ActiveSlotOccupancies.Remove(occupancy);
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new JobCancelResponseData(
                JobId: jobId,
                State: JobState.Canceled,
                CancelRequested: false,
                Message: hasUnresolvedSideEffect
                    ? "Tác vụ đã được hủy bỏ. Do có tác dụng phụ chưa giải quyết, slot occupancy được bảo lưu cho đến khi đối soát an toàn."
                    : "Tác vụ đang chờ đối soát đã được người dùng hủy bỏ.",
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
    /// Tiếp tục tác vụ đang AwaitingUser (job.resume).
    /// Kiểm tra tính đủ điều kiện thông qua Browser Gatekeeper / Session Checkpoint Coordinator.
    /// </summary>
    public async Task<JobResumeResponseData> ResumeAsync(
        string workspaceId,
        string jobId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        await using var dbContext = new TiToAIDbContext(_dbOptions);
        var job = await dbContext.Jobs
            .Include(j => j.ExecutionBinding)
            .FirstOrDefaultAsync(j => j.Id == jobId && j.WorkspaceId == workspaceId, cancellationToken)
            .ConfigureAwait(false);

        if (job is null)
        {
            throw new JobNotFoundException(jobId);
        }

        if (job.State != JobState.AwaitingUser)
        {
            throw new JobConcurrencyConflictException(
                $"Chỉ có thể tiếp tục (resume) tác vụ ở trạng thái 'AwaitingUser', trạng thái hiện tại: '{job.State}'.",
                jobId);
        }

        var profileId = job.ExecutionBinding?.ProfileId;
        bool sideEffectSubmitted = job.SideEffectSubmitted == 1;

        if (!string.IsNullOrWhiteSpace(profileId) && _scheduler.Gatekeeper != null)
        {
            bool eligible = _scheduler.Gatekeeper.CanResumeJob(profileId, jobId, sideEffectSubmitted);
            if (!eligible)
            {
                throw new JobValidationException(
                    "Tác vụ chưa đủ điều kiện tiếp tục: trình duyệt chưa ở trạng thái ẩn (Hidden), phiên chưa kết nối hoặc danh tính không khớp.",
                    jobId);
            }
        }

        bool hasCheckpoint = !string.IsNullOrWhiteSpace(job.ResultText) || !string.IsNullOrWhiteSpace(job.ResultReference);
        string targetState = (sideEffectSubmitted && !hasCheckpoint) ? JobState.AwaitingUser : JobState.Queued;
        job.State = targetState;
        job.ReasonCode = (sideEffectSubmitted && !hasCheckpoint) ? JobReasonCode.ReconcileRequired : null;
        if (targetState == JobState.Queued)
        {
            job.RetryAfterUtc = null;
            job.LeaseExpiresAtUtc = null;
            job.LeaseOwnerId = null;
        }
        job.UpdatedAtUtc = UtcNow.ToString("o");
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _wakeupNotifier?.NotifyWakeup(new JobWakeupSignal("job.resume", workspaceId, jobId, profileId));

        return new JobResumeResponseData(
            JobId: jobId,
            State: targetState,
            Message: (sideEffectSubmitted && !hasCheckpoint)
                ? "Tác vụ có side-effect đã được chuyển sang chế độ đối soát."
                : "Tác vụ đã được tiếp tục và đưa trở lại hàng đợi thành công."
        );
    }

    /// <summary>
    /// Đối soát tác vụ bị treo hoặc yêu cầu người dùng can thiệp (job.reconcile):
    /// Áp dụng cho các tác vụ ở trạng thái AwaitingUser.
    /// Ủy quyền cho JobReconciliationService để đảm bảo tuân thủ đầy đủ execution-contract-v1.1 và giải phóng occupancy.
    /// </summary>
    public Task<JobReconcileResponseData> ReconcileAsync(
        JobReconcileRequest request,
        CancellationToken cancellationToken = default)
    {
        return _reconciliationService.ReconcileAsync(request, cancellationToken);
    }

    /// <summary>
    /// Chạy quét phục hồi sự cố khởi động (3-tier startup recovery sweep).
    /// </summary>
    public Task<int> RunCrashRecoverySweepAsync(CancellationToken cancellationToken = default)
    {
        return _scheduler.RunCrashRecoverySweepAsync(cancellationToken);
    }
}
