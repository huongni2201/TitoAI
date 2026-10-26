using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TiToAI.Contracts.Jobs;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Persistence.Entities;

namespace TiToAI.Application.Jobs;

/// <summary>
/// Bộ điều phối phân phối công bằng (Fair Scheduler), thu hồi lease hết hạn (Lease Sweeper)
/// và bảo vệ hạn ngạch trình duyệt (Browser Tab Quota) theo kiến trúc T15.
/// </summary>
public sealed class JobScheduler : IJobWakeupNotifier, IAsyncDisposable
{
    private readonly DbContextOptions<TiToAIDbContext> _dbOptions;
    private readonly JobReservation _reservation;
    private readonly IJobExecutor _executor;
    private readonly IBrowserGatekeeper _gatekeeper;
    private readonly Func<DateTimeOffset>? _clock;

    private readonly Channel<JobWakeupSignal> _wakeupChannel;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, Task> _runningWorkerTasks = new();
    private Task? _schedulerLoopTask;

    private int _currentWorkspaceIndex = 0;
    private readonly string _schedulerId = $"scheduler-{Guid.NewGuid():N}";

    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(45);
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan FallbackPollInterval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan DrainTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public event Action<JobStateChangedPayload>? OnJobStateChanged;
    public event Action<JobProgressUpdatedPayload>? OnJobProgressUpdated;

    public JobScheduler(
        DbContextOptions<TiToAIDbContext> dbOptions,
        JobReservation reservation,
        IJobExecutor executor,
        IBrowserGatekeeper? gatekeeper = null,
        Func<DateTimeOffset>? clock = null)
    {
        _dbOptions = dbOptions ?? throw new ArgumentNullException(nameof(dbOptions));
        _reservation = reservation ?? throw new ArgumentNullException(nameof(reservation));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _gatekeeper = gatekeeper ?? new DefaultBrowserGatekeeper();
        _clock = clock;

        // Channel đánh thức dạng bounded, nếu đầy sẽ ghi đè signal cũ (DropOldest) để không bao giờ nghẽn bộ nhớ
        var channelOptions = new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        };
        _wakeupChannel = Channel.CreateBounded<JobWakeupSignal>(channelOptions);
    }

    private DateTimeOffset UtcNow => _clock?.Invoke() ?? DateTimeOffset.UtcNow;

    /// <summary>
    /// Gửi tín hiệu đánh thức vào In-Memory Channel để Scheduler lập tức phân phối job mới.
    /// </summary>
    public void NotifyWakeup(JobWakeupSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        _wakeupChannel.Writer.TryWrite(signal);
    }

    /// <summary>
    /// Khởi động vòng lặp điều phối nền của Scheduler.
    /// </summary>
    public void Start()
    {
        if (_schedulerLoopTask is null)
        {
            _schedulerLoopTask = Task.Run(() => SchedulerLoopAsync(_cts.Token));
        }
    }

    /// <summary>
    /// Vòng lặp chính: Đợi tín hiệu Wakeup hoặc timeout FallbackPollInterval (chống starvation).
    /// </summary>
    private async Task SchedulerLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // 1. Quét thu hồi lease và phục hồi sự cố định kỳ
                await RunCrashRecoverySweepAsync(cancellationToken).ConfigureAwait(false);

                // 2. Cố gắng phân phối các job sẵn sàng
                await DispatchPendingJobsAsync(cancellationToken).ConfigureAwait(false);

                // 3. Đợi tín hiệu đánh thức từ Channel hoặc timeout định kỳ
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(FallbackPollInterval);

                try
                {
                    await _wakeupChannel.Reader.WaitToReadAsync(timeoutCts.Token).ConfigureAwait(false);
                    while (_wakeupChannel.Reader.TryRead(out _)) { }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Hết timeout định kỳ: tiếp tục vòng lặp để Fallback Polling
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Log lỗi và nghỉ ngắn tránh spinning loop khi gặp sự cố DB nghiêm trọng
                System.Diagnostics.Debug.WriteLine($"[JobScheduler] Lỗi trong scheduler loop: {ex.Message}");
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Thực hiện phân phối tuần tự đúng 1 Job tiếp theo theo thuật toán Round-Robin giữa các Workspace.
    /// Giúp quan sát thứ tự phân phối công bằng hoặc điều phối tuần tự dưới giới hạn capacity 1 job.
    /// </summary>
    public async Task<bool> DispatchNextSingleJobAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = new TiToAIDbContext(_dbOptions);

        var activeWorkspaces = await dbContext.Workspaces
            .AsNoTracking()
            .Where(w => w.Status == "Active")
            .OrderBy(w => w.Id)
            .Select(w => w.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (activeWorkspaces.Count == 0)
        {
            return false;
        }

        int wsCount = activeWorkspaces.Count;
        int initialIndex = _currentWorkspaceIndex % wsCount;

        for (int i = 0; i < wsCount; i++)
        {
            int targetIndex = (initialIndex + i) % wsCount;
            var targetWorkspaceId = activeWorkspaces[targetIndex];

            bool dispatched = await TryDispatchOneJobForWorkspaceAsync(targetWorkspaceId, cancellationToken).ConfigureAwait(false);
            if (dispatched)
            {
                _currentWorkspaceIndex = (targetIndex + 1) % wsCount;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Thực hiện điều phối công bằng: Duyệt qua các Workspace theo thuật toán Round-Robin.
    /// Mỗi chu kỳ lấy tối đa 1 job cho mỗi Workspace khả dụng.
    /// </summary>
    public async Task<int> DispatchPendingJobsAsync(CancellationToken cancellationToken = default)
    {
        int totalDispatched = 0;

        await using var dbContext = new TiToAIDbContext(_dbOptions);

        // 1. Lấy danh sách toàn bộ Không gian làm việc đang Active
        var activeWorkspaces = await dbContext.Workspaces
            .AsNoTracking()
            .Where(w => w.Status == "Active")
            .OrderBy(w => w.Id)
            .Select(w => w.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (activeWorkspaces.Count == 0)
        {
            return 0;
        }

        int wsCount = activeWorkspaces.Count;

        // Vòng lặp Round-Robin công bằng: Duyệt tuần tự qua các workspace cho đến khi hết capacity
        bool anyDispatchedInRound;
        do
        {
            anyDispatchedInRound = false;
            int initialIndex = _currentWorkspaceIndex % wsCount;

            for (int i = 0; i < wsCount; i++)
            {
                int targetIndex = (initialIndex + i) % wsCount;
                var targetWorkspaceId = activeWorkspaces[targetIndex];

                bool dispatched = await TryDispatchOneJobForWorkspaceAsync(targetWorkspaceId, cancellationToken).ConfigureAwait(false);
                if (dispatched)
                {
                    totalDispatched++;
                    anyDispatchedInRound = true;
                    _currentWorkspaceIndex = (targetIndex + 1) % wsCount;
                }
            }
        } while (anyDispatchedInRound);

        return totalDispatched;
    }

    /// <summary>
    /// Thử cấp quyền và khởi chạy đúng 1 Job hợp lệ cho một Workspace cụ thể.
    /// </summary>
    private async Task<bool> TryDispatchOneJobForWorkspaceAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = new TiToAIDbContext(_dbOptions);
        var nowIso = UtcNow.ToString("o");

        // Tìm các ứng viên Queued của Workspace này
        var candidateJobs = await dbContext.Jobs
            .AsNoTracking()
            .Where(j => j.WorkspaceId == workspaceId &&
                        j.State == JobState.Queued &&
                        (j.RetryAfterUtc == null || string.Compare(j.RetryAfterUtc, nowIso) <= 0))
            .OrderBy(j => j.CreatedAtUtc)
            .Take(5)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var candidate in candidateJobs)
        {
            // Thực hiện Atomic Reservation (BEGIN IMMEDIATE + CAS) từ Global AI Pool v1.1
            var reserveResult = await _reservation.TryReserveJobAsync(
                candidate.Id,
                _schedulerId,
                LeaseDuration,
                isProfileAvailable: profileId =>
                    !_gatekeeper.IsDispatchPaused(profileId) &&
                    !_gatekeeper.IsProfileBusyWithLogin(profileId) &&
                    !_gatekeeper.IsRoleActive(profileId, candidate.TaskType),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (reserveResult.Acquired && reserveResult.Job is not null)
            {
                var assignedProfileId = reserveResult.Binding?.ProfileId ?? "default_profile";
                var assignedAccountId = reserveResult.Binding?.ProviderAccountId;

                // Thông báo chuyển trạng thái Reserved
                NotifyStateChanged(reserveResult.Job.Id, reserveResult.Job.WorkspaceId, reserveResult.Job.ProjectId, JobState.Queued, JobState.Reserved, null, null);

                // Khởi chạy Worker xử lý nền và ghi nhận task vào danh sách quản lý
                var workerJob = reserveResult.Job;
                var workerTask = Task.Run(async () =>
                {
                    try
                    {
                        await ExecuteWorkerAsync(workerJob, assignedProfileId, assignedAccountId).ConfigureAwait(false);
                    }
                    finally
                    {
                        _runningWorkerTasks.TryRemove(workerJob.Id, out _);
                    }
                });

                _runningWorkerTasks.TryAdd(workerJob.Id, workerTask);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Quy trình quét 3 cấp độ (3-Tier Crash Recovery Sweep) phục hồi sự cố và thu hồi Dead Worker:
    /// 1. Ưu tiên 1 (Side-effect Guard): Job đã gửi side-effect -> Chuyển sang AwaitingUser (CẤM retry tự động!).
    /// 2. Ưu tiên 2 (Safe Retry): Job chưa gửi side-effect & còn lượt thử -> Chuyển về Queued kèm retry_after.
    /// 3. Ưu tiên 3 (Exceeded Attempts): Job chưa gửi side-effect & hết lượt -> Chuyển sang Failed.
    /// Thực hiện phục hồi Atomic CAS trên SQLite IMMEDIATE để không ghi đè tiến trình heartbeat/commit đồng thời.
    /// </summary>
    public async Task<int> RunCrashRecoverySweepAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = new TiToAIDbContext(_dbOptions);
        var nowIso = UtcNow.ToString("o");

        // 1. Tìm các job candidates đang Reserved hoặc Running nhưng lease đã quá hạn
        var expiredCandidates = await dbContext.Jobs
            .AsNoTracking()
            .Where(j => (j.State == JobState.Reserved || j.State == JobState.Running) &&
                        j.LeaseExpiresAtUtc != null &&
                        string.Compare(j.LeaseExpiresAtUtc, nowIso) < 0)
            .Select(j => new { j.Id, j.FencingToken })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (expiredCandidates.Count == 0)
        {
            return 0;
        }

        int recoveredCount = 0;

        foreach (var candidate in expiredCandidates)
        {
            await using var txContext = new TiToAIDbContext(_dbOptions);
            var dbConn = txContext.Database.GetDbConnection();
            if (dbConn.State != ConnectionState.Open)
            {
                await dbConn.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            IDbContextTransaction transaction;
            if (dbConn is SqliteConnection sqliteConn)
            {
                var sqliteTx = sqliteConn.BeginTransaction(deferred: false);
                transaction = await txContext.Database.UseTransactionAsync(sqliteTx, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Không thể tạo SqliteTransaction cho crash recovery sweep.");
            }
            else
            {
                transaction = await txContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
            }

            await using (transaction)
            {
                var job = await txContext.Jobs
                    .FirstOrDefaultAsync(j => j.Id == candidate.Id, cancellationToken).ConfigureAwait(false);

                if (job == null || (job.State != JobState.Reserved && job.State != JobState.Running))
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // CAS Guard: Kiểm tra fencing token và lease expiration thực tế để ngăn race condition với heartbeat/submit
                if (job.FencingToken != candidate.FencingToken ||
                    job.LeaseExpiresAtUtc == null ||
                    string.Compare(job.LeaseExpiresAtUtc, nowIso) >= 0)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var prevState = job.State;
                string newState;
                string? reasonCode;

                if (job.SideEffectSubmitted == 1)
                {
                    // Ưu tiên 1: Side-effect Guard (CẤM retry tự động!)
                    newState = JobState.AwaitingUser;
                    reasonCode = JobReasonCode.ReconcileRequired;
                    job.State = newState;
                    job.ReasonCode = reasonCode;
                    job.LeaseExpiresAtUtc = null;
                    job.LeaseOwnerId = null;
                    job.UpdatedAtUtc = nowIso;
                }
                else if (job.Attempts < job.MaxAttempts)
                {
                    // Ưu tiên 2: Chưa gửi side effect và còn lượt thử -> Đưa về Queued kèm giãn cách lũy thừa
                    newState = JobState.Queued;
                    reasonCode = "lease_expired_retry";
                    var retryDelaySeconds = Math.Pow(2, job.Attempts) * 5;
                    job.State = newState;
                    job.ReasonCode = null;
                    job.LeaseExpiresAtUtc = null;
                    job.LeaseOwnerId = null;
                    job.RetryAfterUtc = UtcNow.AddSeconds(retryDelaySeconds).ToString("o");
                    job.UpdatedAtUtc = nowIso;
                }
                else
                {
                    // Ưu tiên 3: Hết số lần thử cho phép -> Failed
                    newState = JobState.Failed;
                    reasonCode = JobReasonCode.LeaseExpiredMaxAttempts;
                    job.State = newState;
                    job.ReasonCode = reasonCode;
                    job.LeaseExpiresAtUtc = null;
                    job.LeaseOwnerId = null;
                    job.UpdatedAtUtc = nowIso;
                }

                await txContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                recoveredCount++;

                // CHỈ PHÁT EVENT SAU KHI TRANSACTION COMMIT THÀNH CÔNG!
                NotifyStateChanged(job.Id, job.WorkspaceId, job.ProjectId, prevState, newState, reasonCode, null);
            }
        }

        return recoveredCount;
    }

    /// <summary>
    /// Vòng đời thực thi tác vụ trên tiến trình Worker:
    /// Quản lý Tab Lease, Heartbeat định kỳ, cam kết cờ Side Effect và commit kết quả cuối cùng.
    /// </summary>
    private async Task ExecuteWorkerAsync(Job reservedJob, string profileId, string? providerAccountId = null)
    {
        string jobId = reservedJob.Id;
        long fencingToken = reservedJob.FencingToken;
        string role = reservedJob.TaskType;
        using var workerCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        IDisposable? tabLease = null;
        Timer? heartbeatTimer = null;
        bool sideEffectCommitted = false;

        try
        {
            // 1. Chiếm giữ Tab Lease độc quyền cho role này TRƯỚC KHI chuyển sang Running và TRƯỚC KHI gọi executor
            tabLease = await _gatekeeper.TryAcquireTabLeaseAsync(profileId, role, jobId, workerCts.Token).ConfigureAwait(false);
            if (tabLease is null)
            {
                // Thất bại chiếm lease (do role tab đang bận):
                // Tuyệt đối KHÔNG thực thi executor! Hoàn trả job về Queued an toàn (không tăng attempts).
                await _reservation.ReleaseReservationDueToNoCapacityAsync(jobId, fencingToken, CancellationToken.None).ConfigureAwait(false);
                NotifyStateChanged(jobId, reservedJob.WorkspaceId, reservedJob.ProjectId, JobState.Reserved, JobState.Queued, "tab_capacity_unavailable", null);
                return;
            }

            // 2. Đã chiếm được lease -> Chuyển trạng thái từ Reserved sang Running
            await _reservation.TransitionToRunningAsync(jobId, fencingToken, workerCts.Token).ConfigureAwait(false);
            NotifyStateChanged(jobId, reservedJob.WorkspaceId, reservedJob.ProjectId, JobState.Reserved, JobState.Running, null, null);

            // 3. Khởi tạo Heartbeat Timer gia hạn lease định kỳ mỗi HeartbeatInterval
            heartbeatTimer = new Timer(async _ =>
            {
                try
                {
                    await _reservation.SendHeartbeatAsync(
                        jobId,
                        _schedulerId,
                        fencingToken,
                        LeaseDuration,
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (FencingTokenStaleException)
                {
                    // Bị loại bỏ bởi worker mới hơn (Zombie Worker) -> Hủy bỏ ngay lập tức tiến trình worker cũ
                    workerCts.Cancel();
                }
                catch
                {
                    // Bỏ qua lỗi tạm thời khi gửi heartbeat
                }
            }, null, HeartbeatInterval, HeartbeatInterval);

            // 4. Chuẩn bị ngữ cảnh thực thi
            var executionContext = new JobExecutionContext(
                JobId: reservedJob.Id,
                WorkspaceId: reservedJob.WorkspaceId,
                ProjectId: reservedJob.ProjectId,
                SceneId: reservedJob.SceneId,
                TaskType: reservedJob.TaskType,
                ProviderKey: reservedJob.ProviderKey,
                StorageRootId: reservedJob.StorageRootId,
                PromptSnapshotText: reservedJob.PromptSnapshotText,
                ConfigSnapshotJson: reservedJob.ConfigSnapshotJson,
                FencingToken: fencingToken,
                Attempts: reservedJob.Attempts,
                ProfileId: profileId,
                ProviderAccountId: providerAccountId
            );

            // 5. Thực thi qua JobExecutor
            var result = await _executor.ExecuteAsync(
                executionContext,
                onPreSideEffectCommitAsync: async () =>
                {
                    // CRITICAL BOUNDARY: Commit cờ side_effect_submitted = 1 TRƯỚC KHI web action click
                    await _reservation.CommitSideEffectAsync(jobId, fencingToken, workerCts.Token).ConfigureAwait(false);
                    sideEffectCommitted = true;
                },
                onProgressAsync: (percent, step) =>
                {
                    OnJobProgressUpdated?.Invoke(new JobProgressUpdatedPayload(
                        JobId: jobId,
                        WorkspaceId: reservedJob.WorkspaceId,
                        ProjectId: reservedJob.ProjectId,
                        SceneId: reservedJob.SceneId,
                        State: JobState.Running,
                        ProgressPercent: percent,
                        CurrentStep: step
                    ));
                    return Task.CompletedTask;
                },
                workerCts.Token).ConfigureAwait(false);

            // 6. Dừng Heartbeat Timer
            if (heartbeatTimer is not null)
            {
                await heartbeatTimer.DisposeAsync().ConfigureAwait(false);
                heartbeatTimer = null;
            }

            // 7. Commit kết quả thành công
            if (result.Success)
            {
                await _reservation.CommitSuccessAsync(
                    jobId,
                    fencingToken,
                    result.ResultReference,
                    result.ResultText,
                    workerCts.Token).ConfigureAwait(false);

                NotifyStateChanged(jobId, reservedJob.WorkspaceId, reservedJob.ProjectId, JobState.Running, JobState.Succeeded, null, result.ResultReference);
            }
            else
            {
                await HandleExecutionFailureAsync(reservedJob, fencingToken, result.ReasonCode ?? "execution_failed", sideEffectCommitted).ConfigureAwait(false);
            }
        }
        catch (FencingTokenStaleException)
        {
            // Zombie worker bị fenced out: Tuyệt đối không can thiệp vào DB nữa
            System.Diagnostics.Debug.WriteLine($"[JobScheduler] Worker {jobId} bị fencing out (zombie). Dừng an toàn.");
        }
        catch (Exception ex)
        {
            await HandleExecutionFailureAsync(reservedJob, fencingToken, ex.Message, sideEffectCommitted).ConfigureAwait(false);
        }
        finally
        {
            if (heartbeatTimer is not null)
            {
                await heartbeatTimer.DisposeAsync().ConfigureAwait(false);
            }

            tabLease?.Dispose();
            _gatekeeper.ReleaseTabLease(profileId, role, jobId);

            // v1.1 §1.3: Khi profile không còn tab/job bận nào khác (idle) thì đóng context an toàn
            if (_gatekeeper.IsProfileIdle(profileId))
            {
                _gatekeeper.CloseProfileIfIdle(profileId);
            }

            // Đánh thức Scheduler ngay lập tức để cấp slot rảnh cho job kế tiếp mà không cần đợi cả đợt
            NotifyWakeup(new JobWakeupSignal("WorkerCompleted", reservedJob.WorkspaceId, jobId));
        }
    }

    private async Task HandleExecutionFailureAsync(Job job, long fencingToken, string reason, bool sideEffectCommitted)
    {
        try
        {
            if (sideEffectCommitted || reason == "adapter_not_available")
            {
                // Đã commit side effect hoặc adapter chưa sẵn sàng: Tuyệt đối không retry mù, chuyển sang AwaitingUser
                await using var dbContext = new TiToAIDbContext(_dbOptions);
                var nowIso = UtcNow.ToString("o");
                var code = sideEffectCommitted ? JobReasonCode.ReconcileRequired : reason;

                await dbContext.Database.ExecuteSqlRawAsync(
                    @"UPDATE Job
                      SET state = 'AwaitingUser',
                          reason_code = @reason,
                          lease_expires_at_utc = NULL,
                          updated_at_utc = @now
                      WHERE id = @id AND fencing_token = @token;",
                    new object[]
                    {
                        new SqliteParameter("@reason", code),
                        new SqliteParameter("@now", nowIso),
                        new SqliteParameter("@id", job.Id),
                        new SqliteParameter("@token", fencingToken)
                    }).ConfigureAwait(false);

                NotifyStateChanged(job.Id, job.WorkspaceId, job.ProjectId, JobState.Running, JobState.AwaitingUser, code, null);
            }
            else
            {
                bool canRetry = job.Attempts < job.MaxAttempts;
                var retryDelay = canRetry ? TimeSpan.FromSeconds(Math.Pow(2, job.Attempts) * 5) : (TimeSpan?)null;

                await _reservation.CommitFailureAsync(
                    job.Id,
                    fencingToken,
                    reason,
                    canRetry,
                    retryDelay,
                    CancellationToken.None).ConfigureAwait(false);

                var newState = canRetry ? JobState.Queued : JobState.Failed;
                NotifyStateChanged(job.Id, job.WorkspaceId, job.ProjectId, JobState.Running, newState, reason, null);
            }
        }
        catch (FencingTokenStaleException)
        {
            // Fenced out, an toàn bỏ qua
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[JobScheduler] Lỗi khi xử lý execution failure: {ex.Message}");
        }
    }

    private void NotifyStateChanged(string jobId, string workspaceId, string? projectId, string prevState, string newState, string? reasonCode, string? resultRef)
    {
        OnJobStateChanged?.Invoke(new JobStateChangedPayload(
            JobId: jobId,
            WorkspaceId: workspaceId,
            ProjectId: projectId,
            PreviousState: prevState,
            NewState: newState,
            ReasonCode: reasonCode,
            ResultReference: resultRef,
            TimestampUtc: UtcNow.ToString("o")
        ));
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _wakeupChannel.Writer.TryComplete();

        if (_schedulerLoopTask is not null)
        {
            try
            {
                await _schedulerLoopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Bỏ qua khi cancel
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[JobScheduler] Lỗi đợi loop: {ex.Message}");
            }
        }

        var runningTasks = _runningWorkerTasks.Values.ToArray();
        if (runningTasks.Length > 0)
        {
            try
            {
                await Task.WhenAll(runningTasks).WaitAsync(DrainTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                System.Diagnostics.Debug.WriteLine("[JobScheduler] Timeout khi drain active worker tasks.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[JobScheduler] Lỗi khi drain workers: {ex.Message}");
            }
        }

        _cts.Dispose();
    }
}
