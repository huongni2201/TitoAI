using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TiToAI.Application.Jobs;
using TiToAI.Application.Settings;
using TiToAI.Contracts.Jobs;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Persistence.Entities;
using TiToAI.Infrastructure.Persistence.Interceptors;
using TiToAI.Infrastructure.Storage;

namespace TiToAI.Desktop.SmokeChecks;

/// <summary>
/// Bộ kiểm thử khói định lượng Task T16:
/// Job Crash Recovery & Reconcile Engine.
/// Bao phủ 9 ca kiểm thử SQLite thật với DB tạm:
/// 1. Crash trước submit (SideEffectSubmitted=0): phục hồi về Queued với retry_after và xóa occupancy.
/// 2. Crash trước submit nhưng hết attempts: phục hồi sang Failed (lease_expired_max_attempts).
/// 3. Crash sau submit (SideEffectSubmitted=1): phục hồi sang AwaitingUser (reconcile_required) và GIỮ NGUYÊN slot occupancy.
/// 4. Hai worker đồng thời chạy recovery sweep trên 1 job: đúng 1 winner thành công, loser không corrupt state.
/// 5. Reconcile resubmit khi SideEffectSubmitted=1 không có ConfirmedNotReceived bị từ chối; có ConfirmedNotReceived=true được đưa về Queued an toàn.
/// 6. Reconcile mark_succeeded không có kết quả bị từ chối; có kết quả chuyển Succeeded và xóa occupancy.
/// 7. Reconcile mark_failed và cancel hoạt động đúng và xóa occupancy.
/// 8. Restart quét DB phục hồi giữ nguyên ExecutionBinding và snapshot.
/// 9. Acceptance 2 capacity=1: Submit -> Lease Expired -> AwaitingUser -> Cancel unresolved side effect giữ occupancy chặn Job mới cho tới khi đối soát an toàn.
/// </summary>
public static class JobCrashRecoverySmokeTests
{
    private static (DbContextOptions<TiToAIDbContext> Options, string TempDir) CreateTestContext()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"titoai_crash_recovery_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var dbPath = Path.Combine(tempDir, "titoai_test.db");
        var connectionString = DatabasePathHelper.BuildConnectionString(dbPath);

        var optionsBuilder = new DbContextOptionsBuilder<TiToAIDbContext>();
        optionsBuilder.UseSqlite(connectionString)
                      .AddInterceptors(new SqlitePragmaInterceptor())
                      .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        var dbOptions = optionsBuilder.Options;

        using (var db = new TiToAIDbContext(dbOptions))
        {
            db.Database.Migrate();

            var now = DateTimeOffset.UtcNow.ToString("o");
            if (!db.BrowserProfiles.Any(p => p.Id == "shared_ai"))
            {
                db.BrowserProfiles.Add(new BrowserProfile
                {
                    Id = "shared_ai",
                    Kind = "SharedAI",
                    ProviderKey = "chatgpt-web",
                    DisplayName = "ChatGPT Pool Account",
                    SessionStatus = "Connected",
                    OperationalState = "Stopped",
                    VisibilityState = "Hidden",
                    Lifecycle = "Active",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
            }

            if (!db.ProviderAccounts.Any(a => a.ProfileId == "shared_ai"))
            {
                db.ProviderAccounts.Add(new ProviderAccount
                {
                    Id = "account-chatgpt-default",
                    ProviderKey = "chatgpt-web",
                    ProfileId = "shared_ai",
                    DisplayName = "ChatGPT Pool Account",
                    VerifiedIdentity = "user@chatgpt.test",
                    IdentityVerifiedAtUtc = now,
                    Enabled = true,
                    Lifecycle = "Active",
                    MaxConcurrentJobs = 3,
                    MaxTabs = 3,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
            }

            db.SaveChanges();
        }

        return (dbOptions, tempDir);
    }

    private static void CleanupTestContext(string tempDir)
    {
        try
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
        catch
        {
            // Bỏ qua lỗi dọn dẹp thư mục tạm
        }
    }

    private static async Task<(string WorkspaceId, string ProjectId, string SceneId)> SeedBasicEntitiesAsync(
        DbContextOptions<TiToAIDbContext> dbOptions)
    {
        await using var db = new TiToAIDbContext(dbOptions);

        var wsId = Guid.NewGuid().ToString();
        var prjId = Guid.NewGuid().ToString();
        var scnId = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToString("o");

        var ws = new Workspace
        {
            Id = wsId,
            Name = "Workspace Crash Test",
            Status = "Active",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.Workspaces.Add(ws);

        var prj = new Project
        {
            Id = prjId,
            WorkspaceId = wsId,
            Name = "Project Crash Test",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.Projects.Add(prj);

        var scn = new Scene
        {
            Id = scnId,
            WorkspaceId = wsId,
            ProjectId = prjId,
            SequenceOrder = 1,
            Name = "Scene 1",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.Scenes.Add(scn);

        await db.SaveChangesAsync();
        return (wsId, prjId, scnId);
    }

    /// <summary>
    /// Test 1: Crash trước submit (SideEffectSubmitted=0): phục hồi về Queued với retry_after và xóa occupancy.
    /// </summary>
    public static async Task Test_CrashBeforeSideEffect_RecoversToQueuedWithRetryAfterAndDeletesOccupancy()
    {
        var (dbOptions, tempDir) = CreateTestContext();
        try
        {
            var (wsId, prjId, scnId) = await SeedBasicEntitiesAsync(dbOptions);
            var jobId = Guid.NewGuid().ToString();
            var now = DateTimeOffset.UtcNow;
            var nowIso = now.ToString("o");
            var expiredLease = now.AddMinutes(-5).ToString("o");

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                db.Jobs.Add(new Job
                {
                    Id = jobId,
                    WorkspaceId = wsId,
                    ProjectId = prjId,
                    SceneId = null,
                    TaskType = JobTaskType.Script,
                    State = JobState.Running,
                    Attempts = 1,
                    MaxAttempts = 3,
                    SideEffectSubmitted = 0,
                    LeaseOwnerId = "worker-crash-1",
                    LeaseExpiresAtUtc = expiredLease,
                    FencingToken = 10,
                    CreatedAtUtc = nowIso,
                    UpdatedAtUtc = nowIso
                });

                db.ActiveSlotOccupancies.Add(new ActiveSlotOccupancy
                {
                    ProviderAccountId = "account-chatgpt-default",
                    TabSlotId = "slot-1",
                    JobId = jobId,
                    LeaseOwnerId = "worker-crash-1",
                    LeaseToken = 10,
                    AcquiredAtUtc = nowIso
                });

                await db.SaveChangesAsync();
            }

            var recoveryService = new JobCrashRecoveryService(dbOptions, clock: () => now);
            var sweepResult = await recoveryService.PerformRecoverySweepAsync();

            if (sweepResult.RecoveredCount != 1)
                throw new Exception($"Kỳ vọng 1 job recovered, thực tế: {sweepResult.RecoveredCount}");
            if (sweepResult.RetriedCount != 1)
                throw new Exception($"Kỳ vọng 1 job retried, thực tế: {sweepResult.RetriedCount}");

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var job = await db.Jobs.FirstAsync(j => j.Id == jobId);
                if (job.State != JobState.Queued)
                    throw new Exception($"Kỳ vọng trạng thái Queued, thực tế: {job.State}");
                if (job.LeaseExpiresAtUtc != null)
                    throw new Exception($"Kỳ vọng LeaseExpiresAtUtc = null, thực tế: {job.LeaseExpiresAtUtc}");
                if (job.LeaseOwnerId != null)
                    throw new Exception($"Kỳ vọng LeaseOwnerId = null, thực tế: {job.LeaseOwnerId}");
                if (string.IsNullOrWhiteSpace(job.RetryAfterUtc))
                    throw new Exception("Kỳ vọng RetryAfterUtc có giá trị.");

                // Kiểm tra giãn cách lũy thừa: attempts = 1 => 2^1 * 5 = 10s
                var retryAfter = DateTimeOffset.Parse(job.RetryAfterUtc);
                var expectedMin = now.AddSeconds(9);
                var expectedMax = now.AddSeconds(11);
                if (retryAfter < expectedMin || retryAfter > expectedMax)
                    throw new Exception($"RetryAfterUtc không đúng độ trễ lũy thừa: {retryAfter}");

                var occExists = await db.ActiveSlotOccupancies.AnyAsync(o => o.JobId == jobId);
                if (occExists)
                    throw new Exception("Kỳ vọng ActiveSlotOccupancy đã bị xóa khỏi DB.");
            }
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// Test 2: Crash trước submit nhưng hết attempts (Attempts >= MaxAttempts): phục hồi sang Failed (lease_expired_max_attempts).
    /// </summary>
    public static async Task Test_CrashBeforeSideEffect_MaxAttemptsExceeded_RecoversToFailedAndDeletesOccupancy()
    {
        var (dbOptions, tempDir) = CreateTestContext();
        try
        {
            var (wsId, prjId, scnId) = await SeedBasicEntitiesAsync(dbOptions);
            var jobId = Guid.NewGuid().ToString();
            var now = DateTimeOffset.UtcNow;
            var nowIso = now.ToString("o");
            var expiredLease = now.AddMinutes(-5).ToString("o");

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                db.Jobs.Add(new Job
                {
                    Id = jobId,
                    WorkspaceId = wsId,
                    ProjectId = prjId,
                    SceneId = null,
                    TaskType = JobTaskType.Script,
                    State = JobState.Reserved,
                    Attempts = 3,
                    MaxAttempts = 3,
                    SideEffectSubmitted = 0,
                    LeaseOwnerId = "worker-crash-2",
                    LeaseExpiresAtUtc = expiredLease,
                    FencingToken = 20,
                    CreatedAtUtc = nowIso,
                    UpdatedAtUtc = nowIso
                });

                db.ActiveSlotOccupancies.Add(new ActiveSlotOccupancy
                {
                    ProviderAccountId = "account-chatgpt-default",
                    TabSlotId = "slot-2",
                    JobId = jobId,
                    LeaseOwnerId = "worker-crash-2",
                    LeaseToken = 20,
                    AcquiredAtUtc = nowIso
                });

                await db.SaveChangesAsync();
            }

            var recoveryService = new JobCrashRecoveryService(dbOptions, clock: () => now);
            var sweepResult = await recoveryService.PerformRecoverySweepAsync();

            if (sweepResult.RecoveredCount != 1)
                throw new Exception($"Kỳ vọng 1 job recovered, thực tế: {sweepResult.RecoveredCount}");
            if (sweepResult.FailedCount != 1)
                throw new Exception($"Kỳ vọng 1 job failed, thực tế: {sweepResult.FailedCount}");

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var job = await db.Jobs.FirstAsync(j => j.Id == jobId);
                if (job.State != JobState.Failed)
                    throw new Exception($"Kỳ vọng trạng thái Failed, thực tế: {job.State}");
                if (job.ReasonCode != JobReasonCode.LeaseExpiredMaxAttempts)
                    throw new Exception($"Kỳ vọng ReasonCode = lease_expired_max_attempts, thực tế: {job.ReasonCode}");
                if (job.LeaseExpiresAtUtc != null)
                    throw new Exception($"Kỳ vọng LeaseExpiresAtUtc = null, thực tế: {job.LeaseExpiresAtUtc}");

                var occExists = await db.ActiveSlotOccupancies.AnyAsync(o => o.JobId == jobId);
                if (occExists)
                    throw new Exception("Kỳ vọng ActiveSlotOccupancy đã bị xóa khi đạt max attempts.");
            }
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// Test 3: Crash sau submit (SideEffectSubmitted=1): phục hồi sang AwaitingUser (reconcile_required) và GIỮ NGUYÊN slot occupancy.
    /// </summary>
    public static async Task Test_CrashAfterSideEffect_RecoversToAwaitingUserAndPreservesOccupancy()
    {
        var (dbOptions, tempDir) = CreateTestContext();
        try
        {
            var (wsId, prjId, scnId) = await SeedBasicEntitiesAsync(dbOptions);
            var jobId = Guid.NewGuid().ToString();
            var now = DateTimeOffset.UtcNow;
            var nowIso = now.ToString("o");
            var expiredLease = now.AddMinutes(-5).ToString("o");

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                db.Jobs.Add(new Job
                {
                    Id = jobId,
                    WorkspaceId = wsId,
                    ProjectId = prjId,
                    SceneId = null,
                    TaskType = JobTaskType.Script,
                    State = JobState.Running,
                    Attempts = 1,
                    MaxAttempts = 3,
                    SideEffectSubmitted = 1,
                    SideEffectAtUtc = now.AddMinutes(-6).ToString("o"),
                    LeaseOwnerId = "worker-crash-3",
                    LeaseExpiresAtUtc = expiredLease,
                    FencingToken = 30,
                    CreatedAtUtc = nowIso,
                    UpdatedAtUtc = nowIso
                });

                db.ActiveSlotOccupancies.Add(new ActiveSlotOccupancy
                {
                    ProviderAccountId = "account-chatgpt-default",
                    TabSlotId = "slot-3",
                    JobId = jobId,
                    LeaseOwnerId = "worker-crash-3",
                    LeaseToken = 30,
                    AcquiredAtUtc = nowIso
                });

                await db.SaveChangesAsync();
            }

            var recoveryService = new JobCrashRecoveryService(dbOptions, clock: () => now);
            var sweepResult = await recoveryService.PerformRecoverySweepAsync();

            if (sweepResult.RecoveredCount != 1)
                throw new Exception($"Kỳ vọng 1 job recovered, thực tế: {sweepResult.RecoveredCount}");
            if (sweepResult.MovedToReconcileCount != 1)
                throw new Exception($"Kỳ vọng 1 job moved to reconcile, thực tế: {sweepResult.MovedToReconcileCount}");

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var job = await db.Jobs.FirstAsync(j => j.Id == jobId);
                if (job.State != JobState.AwaitingUser)
                    throw new Exception($"Kỳ vọng trạng thái AwaitingUser, thực tế: {job.State}");
                if (job.ReasonCode != JobReasonCode.ReconcileRequired)
                    throw new Exception($"Kỳ vọng ReasonCode = reconcile_required, thực tế: {job.ReasonCode}");
                if (job.SideEffectSubmitted != 1)
                    throw new Exception($"Kỳ vọng SideEffectSubmitted = 1, thực tế: {job.SideEffectSubmitted}");

                // BẢO TỒN OCCUPANCY: ActiveSlotOccupancy PHẢI VẪN CÒN NGUYÊN!
                var occ = await db.ActiveSlotOccupancies.FirstOrDefaultAsync(o => o.JobId == jobId);
                if (occ == null)
                    throw new Exception("Kỳ vọng ActiveSlotOccupancy được bảo tồn nguyên vẹn để bảo vệ slot/tab!");
                if (occ.TabSlotId != "slot-3")
                    throw new Exception($"Kỳ vọng TabSlotId = slot-3, thực tế: {occ.TabSlotId}");
            }
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// Test 4: Hai worker đồng thời chạy recovery sweep trên 1 job: đúng 1 winner thành công, loser không corrupt state.
    /// </summary>
    public static async Task Test_ConcurrentRecoverySweeps_ExactlyOneWinnerNoCorruption()
    {
        var (dbOptions, tempDir) = CreateTestContext();
        try
        {
            var (wsId, prjId, scnId) = await SeedBasicEntitiesAsync(dbOptions);
            var jobId = Guid.NewGuid().ToString();
            var now = DateTimeOffset.UtcNow;
            var nowIso = now.ToString("o");
            var expiredLease = now.AddMinutes(-5).ToString("o");

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                db.Jobs.Add(new Job
                {
                    Id = jobId,
                    WorkspaceId = wsId,
                    ProjectId = prjId,
                    SceneId = null,
                    TaskType = JobTaskType.Script,
                    State = JobState.Running,
                    Attempts = 1,
                    MaxAttempts = 3,
                    SideEffectSubmitted = 0,
                    LeaseOwnerId = "worker-racing",
                    LeaseExpiresAtUtc = expiredLease,
                    FencingToken = 40,
                    CreatedAtUtc = nowIso,
                    UpdatedAtUtc = nowIso
                });

                db.ActiveSlotOccupancies.Add(new ActiveSlotOccupancy
                {
                    ProviderAccountId = "account-chatgpt-default",
                    TabSlotId = "slot-1",
                    JobId = jobId,
                    LeaseOwnerId = "worker-racing",
                    LeaseToken = 40,
                    AcquiredAtUtc = nowIso
                });

                await db.SaveChangesAsync();
            }

            var service1 = new JobCrashRecoveryService(dbOptions, clock: () => now);
            var service2 = new JobCrashRecoveryService(dbOptions, clock: () => now);

            var task1 = Task.Run(() => service1.PerformRecoverySweepAsync());
            var task2 = Task.Run(() => service2.PerformRecoverySweepAsync());

            var results = await Task.WhenAll(task1, task2);

            int totalRecovered = results[0].RecoveredCount + results[1].RecoveredCount;
            if (totalRecovered != 1)
            {
                throw new Exception($"Kỳ vọng đúng 1 winner phục hồi job, thực tế: {totalRecovered} (Winner1: {results[0].RecoveredCount}, Winner2: {results[1].RecoveredCount})");
            }

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var job = await db.Jobs.FirstAsync(j => j.Id == jobId);
                if (job.State != JobState.Queued)
                    throw new Exception($"Kỳ vọng trạng thái Queued sau sweep, thực tế: {job.State}");

                var occExists = await db.ActiveSlotOccupancies.AnyAsync(o => o.JobId == jobId);
                if (occExists)
                    throw new Exception("Kỳ vọng ActiveSlotOccupancy đã được xóa sạch.");
            }
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// Test 5: Reconcile resubmit khi SideEffectSubmitted=1 không có ConfirmedNotReceived bị từ chối; có ConfirmedNotReceived=true được đưa về Queued an toàn.
    /// </summary>
    public static async Task Test_ReconcileResubmit_RequiresConfirmedNotReceivedWhenSideEffectSubmitted()
    {
        var (dbOptions, tempDir) = CreateTestContext();
        try
        {
            var (wsId, prjId, scnId) = await SeedBasicEntitiesAsync(dbOptions);
            var jobId = Guid.NewGuid().ToString();
            var now = DateTimeOffset.UtcNow;
            var nowIso = now.ToString("o");

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var job = new Job
                {
                    Id = jobId,
                    WorkspaceId = wsId,
                    ProjectId = prjId,
                    SceneId = null,
                    TaskType = JobTaskType.Script,
                    State = JobState.AwaitingUser,
                    ReasonCode = JobReasonCode.ReconcileRequired,
                    Attempts = 1,
                    MaxAttempts = 3,
                    SideEffectSubmitted = 1,
                    SideEffectAtUtc = nowIso,
                    PromptSnapshotText = "Sample Prompt",
                    ConfigSnapshotJson = "{\"temperature\":0.7}",
                    CreatedAtUtc = nowIso,
                    UpdatedAtUtc = nowIso
                };
                db.Jobs.Add(job);

                db.ExecutionBindings.Add(new ExecutionBinding
                {
                    JobId = jobId,
                    ProviderAccountId = "account-chatgpt-default",
                    ProfileId = "shared_ai",
                    TabSlotId = "slot-1",
                    CapacityConfigRevision = 1,
                    AssignedAtUtc = nowIso
                });

                db.ActiveSlotOccupancies.Add(new ActiveSlotOccupancy
                {
                    ProviderAccountId = "account-chatgpt-default",
                    TabSlotId = "slot-1",
                    JobId = jobId,
                    LeaseOwnerId = "worker-1",
                    LeaseToken = 1,
                    AcquiredAtUtc = nowIso
                });

                await db.SaveChangesAsync();
            }

            var reconcileService = new JobReconciliationService(dbOptions, clock: () => now);

            // 5a. Thử resubmit khi KHÔNG có ConfirmedNotReceived => Phải ném ngoại lệ JobValidationException
            bool threwValidationException = false;
            try
            {
                await reconcileService.ReconcileAsync(new JobReconcileRequest(
                    WorkspaceId: wsId,
                    JobId: jobId,
                    Action: "retry",
                    ConfirmedNotReceived: false
                ));
            }
            catch (JobValidationException)
            {
                threwValidationException = true;
            }

            if (!threwValidationException)
            {
                throw new Exception("Kỳ vọng ném JobValidationException khi resubmit job đã submit side effect mà ConfirmedNotReceived != true!");
            }

            // Kiểm tra state không đổi
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var job = await db.Jobs.FirstAsync(j => j.Id == jobId);
                if (job.State != JobState.AwaitingUser)
                    throw new Exception($"Kỳ vọng job vẫn ở AwaitingUser, thực tế: {job.State}");
                if (job.SideEffectSubmitted != 1)
                    throw new Exception("Kỳ vọng SideEffectSubmitted vẫn là 1.");
            }

            // 5b. Thử resubmit khi CÓ ConfirmedNotReceived == true => Thành công, đưa về Queued
            var res = await reconcileService.ReconcileAsync(new JobReconcileRequest(
                WorkspaceId: wsId,
                JobId: jobId,
                Action: "retry",
                ConfirmedNotReceived: true
            ));

            if (res.CurrentState != JobState.Queued)
                throw new Exception($"Kỳ vọng CurrentState = Queued, thực tế: {res.CurrentState}");

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var job = await db.Jobs.FirstAsync(j => j.Id == jobId);
                if (job.State != JobState.Queued)
                    throw new Exception($"Kỳ vọng DB state = Queued, thực tế: {job.State}");
                if (job.SideEffectSubmitted != 0)
                    throw new Exception("Kỳ vọng SideEffectSubmitted đã được reset về 0.");
                if (job.ReasonCode != null)
                    throw new Exception($"Kỳ vọng ReasonCode = null, thực tế: {job.ReasonCode}");

                // Binding và snapshot vẫn nguyên vẹn
                var binding = await db.ExecutionBindings.FirstAsync(b => b.JobId == jobId);
                if (binding.ProviderAccountId != "account-chatgpt-default" || binding.ProfileId != "shared_ai")
                    throw new Exception("ExecutionBinding bị thay đổi ngoài ý muốn.");
                if (job.PromptSnapshotText != "Sample Prompt")
                    throw new Exception("PromptSnapshotText bị thay đổi ngoài ý muốn.");
            }
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// Test 6: Reconcile mark_succeeded không có kết quả bị từ chối; có kết quả chuyển Succeeded và xóa occupancy.
    /// </summary>
    public static async Task Test_ReconcileMarkSucceeded_RequiresValidResultAndReleasesOccupancy()
    {
        var (dbOptions, tempDir) = CreateTestContext();
        try
        {
            var (wsId, prjId, scnId) = await SeedBasicEntitiesAsync(dbOptions);
            var jobId = Guid.NewGuid().ToString();
            var now = DateTimeOffset.UtcNow;
            var nowIso = now.ToString("o");

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                db.Jobs.Add(new Job
                {
                    Id = jobId,
                    WorkspaceId = wsId,
                    ProjectId = prjId,
                    SceneId = null,
                    TaskType = JobTaskType.Script,
                    State = JobState.AwaitingUser,
                    ReasonCode = JobReasonCode.ReconcileRequired,
                    SideEffectSubmitted = 1,
                    ResultReference = null,
                    ResultText = null,
                    CreatedAtUtc = nowIso,
                    UpdatedAtUtc = nowIso
                });

                db.ActiveSlotOccupancies.Add(new ActiveSlotOccupancy
                {
                    ProviderAccountId = "account-chatgpt-default",
                    TabSlotId = "slot-1",
                    JobId = jobId,
                    LeaseOwnerId = "worker-1",
                    LeaseToken = 1,
                    AcquiredAtUtc = nowIso
                });

                await db.SaveChangesAsync();
            }

            var reconcileService = new JobReconciliationService(dbOptions, clock: () => now);

            // 6a. mark_succeeded không truyền kết quả và job chưa có kết quả => Phải ném ngoại lệ
            bool threwValidationException = false;
            try
            {
                await reconcileService.ReconcileAsync(new JobReconcileRequest(
                    WorkspaceId: wsId,
                    JobId: jobId,
                    Action: "mark_succeeded",
                    ResultReference: "   ",
                    ResultText: ""
                ));
            }
            catch (JobValidationException)
            {
                threwValidationException = true;
            }

            if (!threwValidationException)
            {
                throw new Exception("Kỳ vọng ném JobValidationException khi mark_succeeded không có kết quả hợp lệ!");
            }

            // 6b. mark_succeeded có ResultReference hợp lệ => Thành công, chuyển Succeeded, xóa Occupancy
            var res = await reconcileService.ReconcileAsync(new JobReconcileRequest(
                WorkspaceId: wsId,
                JobId: jobId,
                Action: "mark_succeeded",
                ResultReference: "asset-rec-result-999",
                ResultText: "Resolved by human verification"
            ));

            if (res.CurrentState != JobState.Succeeded)
                throw new Exception($"Kỳ vọng CurrentState = Succeeded, thực tế: {res.CurrentState}");
            if (res.ResultReference != "asset-rec-result-999")
                throw new Exception($"Kỳ vọng ResultReference = asset-rec-result-999, thực tế: {res.ResultReference}");

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var job = await db.Jobs.FirstAsync(j => j.Id == jobId);
                if (job.State != JobState.Succeeded)
                    throw new Exception($"Kỳ vọng DB state = Succeeded, thực tế: {job.State}");
                if (job.ResultReference != "asset-rec-result-999")
                    throw new Exception($"Kỳ vọng DB ResultReference = asset-rec-result-999, thực tế: {job.ResultReference}");
                if (job.ResultText != "Resolved by human verification")
                    throw new Exception($"Kỳ vọng DB ResultText khớp giá trị đã nhập, thực tế: {job.ResultText}");

                var occExists = await db.ActiveSlotOccupancies.AnyAsync(o => o.JobId == jobId);
                if (occExists)
                    throw new Exception("Kỳ vọng ActiveSlotOccupancy đã được giải phóng khi mark_succeeded.");
            }
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// Test 7: Reconcile mark_failed và cancel hoạt động đúng và xóa occupancy.
    /// </summary>
    public static async Task Test_ReconcileMarkFailedAndCancel_WorksProperlyAndReleasesOccupancy()
    {
        var (dbOptions, tempDir) = CreateTestContext();
        try
        {
            var (wsId, prjId, scnId) = await SeedBasicEntitiesAsync(dbOptions);
            var job1Id = Guid.NewGuid().ToString();
            var job2Id = Guid.NewGuid().ToString();
            var now = DateTimeOffset.UtcNow;
            var nowIso = now.ToString("o");

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                db.Jobs.AddRange(
                    new Job
                    {
                        Id = job1Id,
                        WorkspaceId = wsId,
                        ProjectId = prjId,
                        SceneId = null,
                        TaskType = JobTaskType.Script,
                        State = JobState.AwaitingUser,
                        ReasonCode = JobReasonCode.ReconcileRequired,
                        CreatedAtUtc = nowIso,
                        UpdatedAtUtc = nowIso
                    },
                    new Job
                    {
                        Id = job2Id,
                        WorkspaceId = wsId,
                        ProjectId = prjId,
                        SceneId = null,
                        TaskType = JobTaskType.Script,
                        State = JobState.AwaitingUser,
                        ReasonCode = JobReasonCode.ReconcileRequired,
                        CreatedAtUtc = nowIso,
                        UpdatedAtUtc = nowIso
                    }
                );

                db.ActiveSlotOccupancies.AddRange(
                    new ActiveSlotOccupancy
                    {
                        ProviderAccountId = "account-chatgpt-default",
                        TabSlotId = "slot-1",
                        JobId = job1Id,
                        LeaseOwnerId = "worker-1",
                        LeaseToken = 1,
                        AcquiredAtUtc = nowIso
                    },
                    new ActiveSlotOccupancy
                    {
                        ProviderAccountId = "account-chatgpt-default",
                        TabSlotId = "slot-2",
                        JobId = job2Id,
                        LeaseOwnerId = "worker-2",
                        LeaseToken = 1,
                        AcquiredAtUtc = nowIso
                    }
                );

                await db.SaveChangesAsync();
            }

            var reconcileService = new JobReconciliationService(dbOptions, clock: () => now);

            // 7a. mark_failed trên job1
            var res1 = await reconcileService.ReconcileAsync(new JobReconcileRequest(
                WorkspaceId: wsId,
                JobId: job1Id,
                Action: "mark_failed"
            ));

            if (res1.CurrentState != JobState.Failed)
                throw new Exception($"Kỳ vọng Job 1 CurrentState = Failed, thực tế: {res1.CurrentState}");

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var job1 = await db.Jobs.FirstAsync(j => j.Id == job1Id);
                if (job1.State != JobState.Failed)
                    throw new Exception($"Kỳ vọng DB Job 1 state = Failed, thực tế: {job1.State}");
                if (job1.ReasonCode != JobReasonCode.ReconcileRejected)
                    throw new Exception($"Kỳ vọng ReasonCode = reconcile_rejected, thực tế: {job1.ReasonCode}");

                var occ1Exists = await db.ActiveSlotOccupancies.AnyAsync(o => o.JobId == job1Id);
                if (occ1Exists)
                    throw new Exception("Kỳ vọng ActiveSlotOccupancy của Job 1 đã được giải phóng.");
            }

            // 7b. cancel trên job2
            var res2 = await reconcileService.ReconcileAsync(new JobReconcileRequest(
                WorkspaceId: wsId,
                JobId: job2Id,
                Action: "cancel"
            ));

            if (res2.CurrentState != JobState.Canceled)
                throw new Exception($"Kỳ vọng Job 2 CurrentState = Canceled, thực tế: {res2.CurrentState}");

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var job2 = await db.Jobs.FirstAsync(j => j.Id == job2Id);
                if (job2.State != JobState.Canceled)
                    throw new Exception($"Kỳ vọng DB Job 2 state = Canceled, thực tế: {job2.State}");
                if (job2.ReasonCode != JobReasonCode.UserCanceled)
                    throw new Exception($"Kỳ vọng ReasonCode = user_canceled, thực tế: {job2.ReasonCode}");

                var occ2Exists = await db.ActiveSlotOccupancies.AnyAsync(o => o.JobId == job2Id);
                if (occ2Exists)
                    throw new Exception("Kỳ vọng ActiveSlotOccupancy của Job 2 đã được giải phóng.");
            }
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// Test 8: Restart quét DB phục hồi giữ nguyên ExecutionBinding và snapshot.
    /// </summary>
    public static async Task Test_RestartStartupRecovery_PreservesBindingAndSnapshots()
    {
        var (dbOptions, tempDir) = CreateTestContext();
        try
        {
            var (wsId, prjId, scnId) = await SeedBasicEntitiesAsync(dbOptions);
            var jobId = Guid.NewGuid().ToString();
            var now = DateTimeOffset.UtcNow;
            var nowIso = now.ToString("o");
            var expiredLease = now.AddMinutes(-10).ToString("o");

            const string expectedPrompt = "Create a modern cinematic scene for TiToAI introduction.";
            const string expectedConfig = "{\"aspect_ratio\":\"16:9\",\"fps\":30,\"model\":\"gpt-4o-render\"}";

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var job = new Job
                {
                    Id = jobId,
                    WorkspaceId = wsId,
                    ProjectId = prjId,
                    SceneId = null,
                    TaskType = JobTaskType.Script,
                    State = JobState.Running,
                    Attempts = 2,
                    MaxAttempts = 3,
                    SideEffectSubmitted = 1,
                    SideEffectAtUtc = now.AddMinutes(-12).ToString("o"),
                    LeaseOwnerId = "dead-instance-worker",
                    LeaseExpiresAtUtc = expiredLease,
                    FencingToken = 99,
                    PromptSnapshotText = expectedPrompt,
                    ConfigSnapshotJson = expectedConfig,
                    CreatedAtUtc = nowIso,
                    UpdatedAtUtc = nowIso
                };
                db.Jobs.Add(job);

                db.ExecutionBindings.Add(new ExecutionBinding
                {
                    JobId = jobId,
                    ProviderAccountId = "account-chatgpt-default",
                    ProfileId = "shared_ai",
                    TabSlotId = "slot-2",
                    CapacityConfigRevision = 42,
                    AssignedAtUtc = nowIso,
                    LeaseOwnerId = "dead-instance-worker",
                    LeaseToken = 99
                });

                db.ActiveSlotOccupancies.Add(new ActiveSlotOccupancy
                {
                    ProviderAccountId = "account-chatgpt-default",
                    TabSlotId = "slot-2",
                    JobId = jobId,
                    LeaseOwnerId = "dead-instance-worker",
                    LeaseToken = 99,
                    AcquiredAtUtc = nowIso
                });

                await db.SaveChangesAsync();
            }

            // Mô phỏng restart ứng dụng: Tạo instance mới hoàn toàn của service và gọi PerformStartupRecoveryAsync
            var startupRecoveryService = new JobCrashRecoveryService(dbOptions, clock: () => now);
            var sweepResult = await startupRecoveryService.PerformStartupRecoveryAsync();

            if (sweepResult.RecoveredCount != 1)
                throw new Exception($"Kỳ vọng 1 job recovered khi startup, thực tế: {sweepResult.RecoveredCount}");
            if (sweepResult.MovedToReconcileCount != 1)
                throw new Exception($"Kỳ vọng 1 job moved to reconcile, thực tế: {sweepResult.MovedToReconcileCount}");

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var job = await db.Jobs.FirstAsync(j => j.Id == jobId);
                if (job.State != JobState.AwaitingUser)
                    throw new Exception($"Kỳ vọng trạng thái AwaitingUser, thực tế: {job.State}");
                if (job.ReasonCode != JobReasonCode.ReconcileRequired)
                    throw new Exception($"Kỳ vọng ReasonCode = reconcile_required, thực tế: {job.ReasonCode}");

                // KIỂM TRA BẤT BIẾN: ExecutionBinding và Snapshot được bảo tồn nguyên vẹn
                if (job.PromptSnapshotText != expectedPrompt)
                    throw new Exception($"PromptSnapshotText bị sai lệch! Kỳ vọng: '{expectedPrompt}', thực tế: '{job.PromptSnapshotText}'");
                if (job.ConfigSnapshotJson != expectedConfig)
                    throw new Exception($"ConfigSnapshotJson bị sai lệch! Kỳ vọng: '{expectedConfig}', thực tế: '{job.ConfigSnapshotJson}'");

                var binding = await db.ExecutionBindings.FirstOrDefaultAsync(b => b.JobId == jobId);
                if (binding == null)
                    throw new Exception("ExecutionBinding bị mất sau recovery!");
                if (binding.ProviderAccountId != "account-chatgpt-default")
                    throw new Exception($"ProviderAccountId sai lệch: {binding.ProviderAccountId}");
                if (binding.ProfileId != "shared_ai")
                    throw new Exception($"ProfileId sai lệch: {binding.ProfileId}");
                if (binding.TabSlotId != "slot-2")
                    throw new Exception($"TabSlotId sai lệch: {binding.TabSlotId}");

                // ActiveSlotOccupancy vẫn còn nguyên
                var occ = await db.ActiveSlotOccupancies.FirstOrDefaultAsync(o => o.JobId == jobId);
                if (occ == null)
                    throw new Exception("ActiveSlotOccupancy bị mất sau startup recovery!");
                if (occ.TabSlotId != "slot-2")
                    throw new Exception($"Occupancy TabSlotId sai lệch: {occ.TabSlotId}");
            }
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// Test 9: Acceptance 2 capacity=1: Submit -> Lease Expired -> AwaitingUser -> Cancel unresolved side effect
    /// giữ nguyên ActiveSlotOccupancy và chặn Job mới cho tới khi đối soát an toàn (ConfirmedNotReceived == true).
    /// </summary>
    public static async Task Test9_Capacity1_Submit_LeaseExpired_AwaitingUser_Cancel_BlocksNewJobUntilReconciled()
    {
        var (dbOptions, tempDir) = CreateTestContext();
        try
        {
            var (wsId, prjId, scnId) = await SeedBasicEntitiesAsync(dbOptions);
            var now = DateTimeOffset.UtcNow;
            var nowIso = now.ToString("o");
            var expiredLease = now.AddMinutes(-10).ToString("o");

            const string accountId = "account-chatgpt-default";
            const string profileId = "shared_ai";

            // 1. Cấu hình ProviderAccount với MaxConcurrentJobs = 1, MaxTabs = 1
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var account = await db.ProviderAccounts.FirstAsync(a => a.Id == accountId);
                account.MaxConcurrentJobs = 1;
                account.MaxTabs = 1;
                await db.SaveChangesAsync();
            }

            // 2. Job 1 chạy và submit side-effect (SideEffectSubmitted = 1)
            var job1Id = Guid.NewGuid().ToString();
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                db.Jobs.Add(new Job
                {
                    Id = job1Id,
                    WorkspaceId = wsId,
                    ProjectId = prjId,
                    SceneId = null,
                    TaskType = JobTaskType.Script,
                    ProviderKey = "chatgpt-web",
                    State = JobState.Running,
                    Attempts = 1,
                    MaxAttempts = 3,
                    SideEffectSubmitted = 1,
                    SideEffectAtUtc = now.AddMinutes(-15).ToString("o"),
                    LeaseOwnerId = "worker-1",
                    LeaseExpiresAtUtc = expiredLease,
                    FencingToken = 10,
                    CreatedAtUtc = nowIso,
                    UpdatedAtUtc = nowIso
                });

                db.ExecutionBindings.Add(new ExecutionBinding
                {
                    JobId = job1Id,
                    ProviderAccountId = accountId,
                    ProfileId = profileId,
                    TabSlotId = "slot-1",
                    CapacityConfigRevision = 1,
                    AssignedAtUtc = nowIso,
                    LeaseOwnerId = "worker-1",
                    LeaseToken = 10
                });

                db.ActiveSlotOccupancies.Add(new ActiveSlotOccupancy
                {
                    ProviderAccountId = accountId,
                    TabSlotId = "slot-1",
                    JobId = job1Id,
                    LeaseOwnerId = "worker-1",
                    LeaseToken = 10,
                    AcquiredAtUtc = nowIso
                });

                await db.SaveChangesAsync();
            }

            // 3. Lease hết hạn -> Quét recovery (PerformRecoverySweepAsync):
            // Job 1 chuyển sang AwaitingUser với reconcile_required. Occupancy của Job 1 vẫn được giữ nguyên.
            var recoveryService = new JobCrashRecoveryService(dbOptions, clock: () => now);
            var sweepResult = await recoveryService.PerformRecoverySweepAsync();

            if (sweepResult.MovedToReconcileCount != 1)
                throw new Exception($"Kỳ vọng 1 job chuyển sang Reconcile, thực tế: {sweepResult.MovedToReconcileCount}");

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var j1 = await db.Jobs.FirstAsync(j => j.Id == job1Id);
                if (j1.State != JobState.AwaitingUser)
                    throw new Exception($"Kỳ vọng Job 1 State = AwaitingUser, thực tế: {j1.State}");
                if (j1.ReasonCode != JobReasonCode.ReconcileRequired)
                    throw new Exception($"Kỳ vọng Job 1 ReasonCode = reconcile_required, thực tế: {j1.ReasonCode}");

                var occ1 = await db.ActiveSlotOccupancies.FirstOrDefaultAsync(o => o.JobId == job1Id);
                if (occ1 == null)
                    throw new Exception("Kỳ vọng ActiveSlotOccupancy của Job 1 được giữ nguyên sau recovery sweep.");
            }

            // 4. Người dùng hủy Job 1 (action: "cancel", ConfirmedNotReceived = false/null). Job 1 chuyển Canceled.
            // Do side-effect chưa được giải quyết, ActiveSlotOccupancy của Job 1 VẪN BẮT BUỘC ĐƯỢC GIỮ NGUYÊN!
            var reconcileService = new JobReconciliationService(dbOptions, clock: () => now);
            var cancelRes = await reconcileService.ReconcileAsync(new JobReconcileRequest(
                WorkspaceId: wsId,
                JobId: job1Id,
                Action: "cancel",
                ConfirmedNotReceived: false
            ));

            if (cancelRes.CurrentState != JobState.Canceled)
                throw new Exception($"Kỳ vọng Job 1 CurrentState = Canceled, thực tế: {cancelRes.CurrentState}");

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var j1 = await db.Jobs.FirstAsync(j => j.Id == job1Id);
                if (j1.State != JobState.Canceled)
                    throw new Exception($"Kỳ vọng Job 1 trong DB có State = Canceled, thực tế: {j1.State}");
                if (j1.ReasonCode != JobReasonCode.UserCanceled)
                    throw new Exception($"Kỳ vọng Job 1 trong DB có ReasonCode = user_canceled, thực tế: {j1.ReasonCode}");

                var occ1 = await db.ActiveSlotOccupancies.FirstOrDefaultAsync(o => o.JobId == job1Id);
                if (occ1 == null)
                    throw new Exception("BẢO VỆ CAPACITY: ActiveSlotOccupancy của Job 1 VẪN BẮT BUỘC ĐƯỢC GIỮ NGUYÊN khi cancel unresolved side-effect!");
            }

            // 5. Tạo Job 2 trên cùng provider/account. Thử reserve (TryReserveJobAsync).
            // Khẳng định: reserveResult.Acquired == false (bị chặn vì account capacity = 1 đã bị chiếm bởi occupancy chưa giải quyết của Job 1, cleanup orphan không được xóa occupancy này!).
            var job2Id = Guid.NewGuid().ToString();
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                db.Jobs.Add(new Job
                {
                    Id = job2Id,
                    WorkspaceId = wsId,
                    ProjectId = prjId,
                    SceneId = null,
                    TaskType = JobTaskType.Script,
                    ProviderKey = "chatgpt-web",
                    State = JobState.Queued,
                    Attempts = 0,
                    MaxAttempts = 3,
                    SideEffectSubmitted = 0,
                    CreatedAtUtc = nowIso,
                    UpdatedAtUtc = nowIso
                });
                await db.SaveChangesAsync();
            }

            var reservationService = new JobReservation(dbOptions, () => now);
            var reserveResult1 = await reservationService.TryReserveJobAsync(job2Id, "worker-2", TimeSpan.FromMinutes(5));

            if (reserveResult1.Acquired)
                throw new Exception("Kỳ vọng reserve Job 2 thất bại vì account capacity = 1 đã bị chiếm bởi Job 1 chưa giải quyết side-effect!");

            // Kiểm tra lại occupancy của Job 1 không bị xóa bởi orphan cleanup trong TryReserveJobAsync
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var occ1StillExists = await db.ActiveSlotOccupancies.AnyAsync(o => o.JobId == job1Id);
                if (!occ1StillExists)
                    throw new Exception("ActiveSlotOccupancy của Job 1 bị orphan cleanup xóa trái phép trong TryReserveJobAsync!");
            }

            // 6. Thực hiện đối soát an toàn cho Job 1: gọi ReconcileAsync với action: "cancel", ConfirmedNotReceived: true.
            // Khẳng định: ActiveSlotOccupancy của Job 1 bây giờ mới được giải phóng!
            var safeReconcileRes = await reconcileService.ReconcileAsync(new JobReconcileRequest(
                WorkspaceId: wsId,
                JobId: job1Id,
                Action: "cancel",
                ConfirmedNotReceived: true
            ));

            if (safeReconcileRes.CurrentState != JobState.Canceled)
                throw new Exception($"Kỳ vọng Job 1 sau đối soát an toàn vẫn có CurrentState = Canceled, thực tế: {safeReconcileRes.CurrentState}");

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var occ1AfterSafeReconcile = await db.ActiveSlotOccupancies.FirstOrDefaultAsync(o => o.JobId == job1Id);
                if (occ1AfterSafeReconcile != null)
                    throw new Exception("Kỳ vọng ActiveSlotOccupancy của Job 1 ĐƯỢC GIẢI PHÓNG sau khi đối soát an toàn với ConfirmedNotReceived == true.");
            }

            // 7. Thử reserve lại Job 2:
            // Khẳng định: reserveResult.Acquired == true!
            var reserveResult2 = await reservationService.TryReserveJobAsync(job2Id, "worker-2", TimeSpan.FromMinutes(5));
            if (!reserveResult2.Acquired)
                throw new Exception($"Kỳ vọng Job 2 reserve thành công sau khi Job 1 đã được giải phóng slot, nhưng thất bại: {reserveResult2.Reason}");

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var occ2 = await db.ActiveSlotOccupancies.FirstOrDefaultAsync(o => o.JobId == job2Id);
                if (occ2 == null)
                    throw new Exception("Kỳ vọng Job 2 đã chiếm slot occupancy sau khi reserve thành công!");
                if (occ2.ProviderAccountId != accountId)
                    throw new Exception($"Kỳ vọng Job 2 dùng account '{accountId}', thực tế: {occ2.ProviderAccountId}");
            }
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// Test 10: Reconcile kết quả muộn sau cancel:
    /// - JobService.CancelAsync trên job AwaitingUser đã submit side effect: chuyển Canceled và giữ occupancy.
    /// - Sau đó kết quả muộn đến và người dùng gọi ReconcileAsync (resolve / mark_succeeded):
    ///   + BẮT BUỘC giữ nguyên trạng thái Canceled (KHÔNG chuyển Succeeded).
    ///   + Chuyển Asset thành "Unlinked" (Status = "Unlinked", SceneId = null).
    ///   + KHÔNG tự động áp kết quả vào Scene (scene.ImageAssetId vẫn là null).
    ///   + Giải phóng ActiveSlotOccupancy an toàn sau khi đã ghi nhận kết quả.
    /// </summary>
    public static async Task Test10_LateResultAfterCancel_KeepsCanceled_UnlinksAsset_DoesNotApplyToScene()
    {
        var (dbOptions, tempDir) = CreateTestContext();
        try
        {
            var (wsId, prjId, scnId) = await SeedBasicEntitiesAsync(dbOptions);
            var now = DateTimeOffset.UtcNow;
            var nowIso = now.ToString("o");
            var expiredLease = now.AddMinutes(-10).ToString("o");

            const string accountId = "account-chatgpt-default";
            const string profileId = "shared_ai";
            var jobId = Guid.NewGuid().ToString("D");

            // 1. Tạo Job đang Running đã submit side-effect gắn với Scene
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                db.Jobs.Add(new Job
                {
                    Id = jobId,
                    WorkspaceId = wsId,
                    ProjectId = prjId,
                    SceneId = scnId,
                    TaskType = JobTaskType.Image,
                    ProviderKey = "chatgpt-web",
                    State = JobState.Running,
                    Attempts = 1,
                    MaxAttempts = 3,
                    SideEffectSubmitted = 1,
                    SideEffectAtUtc = now.AddMinutes(-15).ToString("o"),
                    LeaseOwnerId = "worker-1",
                    LeaseExpiresAtUtc = expiredLease,
                    FencingToken = 10,
                    CreatedAtUtc = nowIso,
                    UpdatedAtUtc = nowIso
                });

                db.ExecutionBindings.Add(new ExecutionBinding
                {
                    JobId = jobId,
                    ProviderAccountId = accountId,
                    ProfileId = profileId,
                    TabSlotId = "slot-1",
                    CapacityConfigRevision = 1,
                    AssignedAtUtc = nowIso,
                    LeaseOwnerId = "worker-1",
                    LeaseToken = 10
                });

                db.ActiveSlotOccupancies.Add(new ActiveSlotOccupancy
                {
                    ProviderAccountId = accountId,
                    TabSlotId = "slot-1",
                    JobId = jobId,
                    LeaseOwnerId = "worker-1",
                    LeaseToken = 10,
                    AcquiredAtUtc = nowIso
                });

                await db.SaveChangesAsync();
            }

            // 2. Lease hết hạn -> Quét recovery đưa sang AwaitingUser (occupancy bảo lưu)
            var recoveryService = new JobCrashRecoveryService(dbOptions, clock: () => now);
            var sweepResult = await recoveryService.PerformRecoverySweepAsync();
            if (sweepResult.MovedToReconcileCount != 1)
            {
                throw new Exception($"Kỳ vọng 1 job chuyển sang Reconcile, thực tế: {sweepResult.MovedToReconcileCount}");
            }

            // 3. Khởi tạo JobService qua production composition
            var reservation = new JobReservation(dbOptions, () => now);
            await using var scheduler = new JobScheduler(dbOptions, reservation, new DeterministicFakeJobExecutor(), clock: () => now);
            var rootRegistry = new StorageRootRegistry(dbOptions);
            var settingsService = new SettingsService(dbOptions, rootRegistry);
            var snapshotFactory = new JobSnapshotFactory(settingsService);
            var jobService = new JobService(dbOptions, snapshotFactory, scheduler, clock: () => now);

            // 4. Người dùng gọi JobService.CancelAsync
            var cancelRes = await jobService.CancelAsync(wsId, jobId);
            if (cancelRes.State != JobState.Canceled)
            {
                throw new Exception($"Kỳ vọng CancelAsync trả về Canceled, thực tế: {cancelRes.State}");
            }

            // Kiểm tra DB: Job ở trạng thái Canceled, CancelRequested = 1, và ActiveSlotOccupancy VẪN ĐƯỢC GIỮ NGUYÊN (unresolved side-effect)
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var j = await db.Jobs.FirstAsync(x => x.Id == jobId);
                if (j.State != JobState.Canceled)
                    throw new Exception($"Kỳ vọng Job trong DB = Canceled, thực tế: {j.State}");
                if (j.CancelRequested != 1)
                    throw new Exception($"Kỳ vọng Job trong DB có CancelRequested = 1, thực tế: {j.CancelRequested}");
                if (j.ReasonCode != JobReasonCode.UserCanceled)
                    throw new Exception($"Kỳ vọng ReasonCode = user_canceled, thực tế: {j.ReasonCode}");

                var occ = await db.ActiveSlotOccupancies.FirstOrDefaultAsync(o => o.JobId == jobId);
                if (occ == null)
                    throw new Exception("ActiveSlotOccupancy phải được giữ nguyên sau khi cancel tác vụ có unresolved side-effect!");
            }

            // 5. Kết quả muộn (Late Result) đến từ provider: tạo bản ghi Asset trong DB
            var assetId = Guid.NewGuid().ToString("N");
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                db.Assets.Add(new Asset
                {
                    Id = assetId,
                    WorkspaceId = wsId,
                    ProjectId = prjId,
                    AssetType = "Image",
                    StorageRootId = "root_default",
                    RelativePath = $"{wsId}/images/{assetId}.png",
                    ByteSize = 1024,
                    Sha256Hash = "abc123hash",
                    Status = "Linked",
                    CreatedAtUtc = nowIso
                });
                await db.SaveChangesAsync();
            }

            // 6. Gọi JobService.ReconcileAsync với action = "resolve" (hoặc "mark_succeeded")
            var reconcileRes = await jobService.ReconcileAsync(new JobReconcileRequest(
                WorkspaceId: wsId,
                JobId: jobId,
                Action: "resolve",
                ResultReference: assetId
            ));

            // KHẲNG ĐỊNH 1: Trạng thái trả về BẮT BUỘC là Canceled (TUYỆT ĐỐI KHÔNG chuyển Succeeded!)
            if (reconcileRes.CurrentState != JobState.Canceled)
            {
                throw new Exception($"Kỳ vọng Reconcile kết quả muộn giữ Canceled, thực tế bị chuyển sang: {reconcileRes.CurrentState}");
            }

            // KHẲNG ĐỊNH 2: Kiểm tra toàn vẹn DB
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var j = await db.Jobs.FirstAsync(x => x.Id == jobId);
                if (j.State != JobState.Canceled)
                {
                    throw new Exception($"Job trong DB phải giữ nguyên trạng thái Canceled! Thực tế: {j.State}");
                }
                if (j.ReasonCode != JobReasonCode.UserCanceled)
                {
                    throw new Exception($"ReasonCode phải là user_canceled! Thực tế: {j.ReasonCode}");
                }
                if (j.ResultReference != assetId)
                {
                    throw new Exception($"ResultReference phải được ghi nhận: {j.ResultReference}");
                }

                // KHẲNG ĐỊNH 3: Asset chuyển sang Unlinked
                var asset = await db.Assets.FirstAsync(a => a.Id == assetId);
                if (asset.Status != "Unlinked")
                {
                    throw new Exception($"Asset phải chuyển sang Status = 'Unlinked'! Thực tế: {asset.Status}");
                }

                // KHẲNG ĐỊNH 4: Scene KHÔNG bị tự động áp kết quả vào ImageAssetId
                var scene = await db.Scenes.FirstAsync(s => s.Id == scnId);
                if (scene.ImageAssetId == assetId)
                {
                    throw new Exception("Scene TUYỆT ĐỐI KHÔNG được tự áp Asset vào ImageAssetId khi Job đã bị hủy!");
                }

                // KHẲNG ĐỊNH 5: ActiveSlotOccupancy đã được giải phóng an toàn
                var occ = await db.ActiveSlotOccupancies.FirstOrDefaultAsync(o => o.JobId == jobId);
                if (occ != null)
                {
                    throw new Exception("ActiveSlotOccupancy phải được giải phóng sau khi đối soát kết quả muộn thành công!");
                }
            }
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// Test 11 (Regression Q1): Job Canceled unresolved phải xuất hiện trong job.list_active,
    /// và sau khi đối soát an toàn (reconcile) thì biến mất khỏi list_active đồng thời giải phóng slot rảnh.
    /// Kịch bản: submit -> recovery -> cancel -> service mới list_active vẫn thấy job -> reconcile -> không còn active và slot rảnh.
    /// </summary>
    public static async Task Test11_CanceledUnresolvedJob_ListedInListActive_RemovedAfterReconcileAndSlotFreed()
    {
        var (dbOptions, tempDir) = CreateTestContext();
        try
        {
            var (wsId, prjId, scnId) = await SeedBasicEntitiesAsync(dbOptions);
            var now = DateTimeOffset.UtcNow;
            var nowIso = now.ToString("o");
            var expiredLease = now.AddMinutes(-10).ToString("o");

            const string accountId = "account-chatgpt-default";
            const string profileId = "shared_ai";
            var job1Id = Guid.NewGuid().ToString("D");

            // 1. Cấu hình ProviderAccount với MaxConcurrentJobs = 1, MaxTabs = 1
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var account = await db.ProviderAccounts.FirstAsync(a => a.Id == accountId);
                account.MaxConcurrentJobs = 1;
                account.MaxTabs = 1;

                // Tạo Job 1 đang Running và submit side-effect
                db.Jobs.Add(new Job
                {
                    Id = job1Id,
                    WorkspaceId = wsId,
                    ProjectId = prjId,
                    SceneId = scnId,
                    TaskType = JobTaskType.Image,
                    ProviderKey = "chatgpt-web",
                    State = JobState.Running,
                    Attempts = 1,
                    MaxAttempts = 3,
                    SideEffectSubmitted = 1,
                    SideEffectAtUtc = now.AddMinutes(-15).ToString("o"),
                    LeaseOwnerId = "worker-1",
                    LeaseExpiresAtUtc = expiredLease,
                    FencingToken = 10,
                    CreatedAtUtc = nowIso,
                    UpdatedAtUtc = nowIso
                });

                db.ExecutionBindings.Add(new ExecutionBinding
                {
                    JobId = job1Id,
                    ProviderAccountId = accountId,
                    ProfileId = profileId,
                    TabSlotId = "slot-1",
                    CapacityConfigRevision = 1,
                    AssignedAtUtc = nowIso,
                    LeaseOwnerId = "worker-1",
                    LeaseToken = 10
                });

                db.ActiveSlotOccupancies.Add(new ActiveSlotOccupancy
                {
                    ProviderAccountId = accountId,
                    TabSlotId = "slot-1",
                    JobId = job1Id,
                    LeaseOwnerId = "worker-1",
                    LeaseToken = 10,
                    AcquiredAtUtc = nowIso
                });

                await db.SaveChangesAsync();
            }

            // 2. Lease hết hạn -> Quét recovery đưa sang AwaitingUser (occupancy bảo lưu)
            var recoveryService = new JobCrashRecoveryService(dbOptions, clock: () => now);
            var sweepResult = await recoveryService.PerformRecoverySweepAsync();
            if (sweepResult.MovedToReconcileCount != 1)
            {
                throw new Exception($"Kỳ vọng 1 job chuyển sang Reconcile, thực tế: {sweepResult.MovedToReconcileCount}");
            }

            // 3. Khởi tạo JobService qua production composition
            var reservation = new JobReservation(dbOptions, () => now);
            await using var scheduler = new JobScheduler(dbOptions, reservation, new DeterministicFakeJobExecutor(), clock: () => now);
            var rootRegistry = new StorageRootRegistry(dbOptions);
            var settingsService = new SettingsService(dbOptions, rootRegistry);
            var snapshotFactory = new JobSnapshotFactory(settingsService);
            var jobService = new JobService(dbOptions, snapshotFactory, scheduler, clock: () => now);

            // 4. Người dùng gọi JobService.CancelAsync
            var cancelRes = await jobService.CancelAsync(wsId, job1Id);
            if (cancelRes.State != JobState.Canceled)
            {
                throw new Exception($"Kỳ vọng CancelAsync trả về Canceled, thực tế: {cancelRes.State}");
            }

            // 5. MÔ PHỎNG RESTART / KHỞI TẠO SERVICE MỚI
            var freshJobService = new JobService(dbOptions, snapshotFactory, scheduler, clock: () => now);

            // KHẲNG ĐỊNH 1: list_active từ service mới BẮT BUỘC phải thấy Job 1 (vì Canceled còn unresolved side effect & occupancy)!
            var activeListBeforeReconcile = await freshJobService.ListActiveAsync(wsId);
            var foundJob1 = activeListBeforeReconcile.Jobs.FirstOrDefault(j => j.Id == job1Id);
            if (foundJob1 == null)
            {
                throw new Exception("Job Canceled unresolved BẮT BUỘC phải xuất hiện trong list_active theo execution-contract-v1.1 §5!");
            }

            // 6. Đối soát an toàn qua JobReconciliationService (action: "cancel" với ConfirmedNotReceived = true)
            var reconcileService = new JobReconciliationService(dbOptions, clock: () => now);
            var reconcileRes = await reconcileService.ReconcileAsync(new JobReconcileRequest(
                WorkspaceId: wsId,
                JobId: job1Id,
                Action: "cancel",
                ConfirmedNotReceived: true
            ));

            if (reconcileRes.CurrentState != JobState.Canceled)
            {
                throw new Exception($"Reconcile cancel phải giữ nguyên trạng thái Canceled! Thực tế: {reconcileRes.CurrentState}");
            }

            // KHẲNG ĐỊNH 2: list_active sau reconcile KHÔNG ĐƯỢC chứa Job 1 nữa (đã resolved)!
            var activeListAfterReconcile = await freshJobService.ListActiveAsync(wsId);
            var notFoundJob1 = activeListAfterReconcile.Jobs.FirstOrDefault(j => j.Id == job1Id);
            if (notFoundJob1 != null)
            {
                throw new Exception("Job Canceled sau khi reconcile an toàn PHẢI BIẾN MẤT khỏi list_active!");
            }

            // KHẲNG ĐỊNH 3: ActiveSlotOccupancy đã được giải phóng (slot rảnh)
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var occAfter = await db.ActiveSlotOccupancies.FirstOrDefaultAsync(o => o.JobId == job1Id);
                if (occAfter != null)
                {
                    throw new Exception("ActiveSlotOccupancy phải được giải phóng sau đối soát an toàn!");
                }
            }

            // KHẲNG ĐỊNH 4: Capacity=1 rảnh, Job 2 có thể reserve thành công ngay lập tức
            var job2Id = Guid.NewGuid().ToString("D");
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                db.Jobs.Add(new Job
                {
                    Id = job2Id,
                    WorkspaceId = wsId,
                    ProjectId = prjId,
                    SceneId = scnId,
                    TaskType = JobTaskType.Image,
                    ProviderKey = "chatgpt-web",
                    State = JobState.Queued,
                    Attempts = 0,
                    MaxAttempts = 3,
                    SideEffectSubmitted = 0,
                    CreatedAtUtc = nowIso,
                    UpdatedAtUtc = nowIso
                });
                await db.SaveChangesAsync();
            }

            var reserveRes2 = await reservation.TryReserveJobAsync(job2Id, "worker_2", TimeSpan.FromMinutes(5), cancellationToken: CancellationToken.None);
            if (!reserveRes2.Acquired || reserveRes2.Job?.Id != job2Id)
            {
                throw new Exception("Job 2 phải chiếm được slot ngay sau khi Job 1 được giải phóng!");
            }
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// Test 12 (Regression Q2): Tác vụ đã hủy (Canceled hoặc CancelRequested=1):
    /// - Chặn đứng retry/resubmit (kể cả khi ConfirmedNotReceived = true), không làm sống lại thành lần sinh AI mới.
    /// - Chặn finalize khi không có checkpoint.
    /// - Khi có checkpoint: finalize qua production giữ nguyên ý định Canceled, lưu asset Unlinked, không áp vào scene và không gọi AI lại.
    /// </summary>
    public static async Task Test12_CanceledJob_ReconcileRetryAndResubmitBlocked_FinalizePreservesCanceledIntent()
    {
        var (dbOptions, tempDir) = CreateTestContext();
        try
        {
            var (wsId, prjId, scnId) = await SeedBasicEntitiesAsync(dbOptions);
            var now = DateTimeOffset.UtcNow;
            var nowIso = now.ToString("o");
            var expiredLease = now.AddMinutes(-10).ToString("o");

            const string accountId = "account-chatgpt-default";
            const string profileId = "shared_ai";
            var jobId = Guid.NewGuid().ToString("D");

            // Cấu hình storage root trong DB
            var rootDir = Path.Combine(tempDir, "storage_root");
            Directory.CreateDirectory(rootDir);
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                db.AppSettings.Add(new AppSetting
                {
                    Key = "storage.roots.root_default",
                    Value = rootDir,
                    UpdatedAtUtc = nowIso
                });

                // Job đang Running và đã submit side-effect
                db.Jobs.Add(new Job
                {
                    Id = jobId,
                    WorkspaceId = wsId,
                    ProjectId = prjId,
                    SceneId = scnId,
                    TaskType = JobTaskType.Image,
                    ProviderKey = "chatgpt-web",
                    StorageRootId = "root_default",
                    PromptSnapshotText = "Prompt test 12",
                    ConfigSnapshotJson = "{}",
                    State = JobState.Running,
                    Attempts = 1,
                    MaxAttempts = 3,
                    SideEffectSubmitted = 1,
                    SideEffectAtUtc = now.AddMinutes(-15).ToString("o"),
                    LeaseOwnerId = "worker-1",
                    LeaseExpiresAtUtc = expiredLease,
                    FencingToken = 10,
                    CreatedAtUtc = nowIso,
                    UpdatedAtUtc = nowIso
                });

                db.ExecutionBindings.Add(new ExecutionBinding
                {
                    JobId = jobId,
                    ProviderAccountId = accountId,
                    ProfileId = profileId,
                    TabSlotId = "slot-1",
                    CapacityConfigRevision = 1,
                    AssignedAtUtc = nowIso,
                    LeaseOwnerId = "worker-1",
                    LeaseToken = 10
                });

                db.ActiveSlotOccupancies.Add(new ActiveSlotOccupancy
                {
                    ProviderAccountId = accountId,
                    TabSlotId = "slot-1",
                    JobId = jobId,
                    LeaseOwnerId = "worker-1",
                    LeaseToken = 10,
                    AcquiredAtUtc = nowIso
                });

                await db.SaveChangesAsync();
            }

            // Crash recovery -> AwaitingUser
            var recoveryService = new JobCrashRecoveryService(dbOptions, clock: () => now);
            await recoveryService.PerformRecoverySweepAsync();

            var rootRegistry = new StorageRootRegistry(dbOptions);
            var fileStore = new AssetFileStore();
            var fakeExecutor = new DeterministicFakeJobExecutor
            {
                FileStore = fileStore,
                RootRegistry = rootRegistry,
                FinalizeRelativePath = $"{wsId}/images/output_test12.png"
            };

            var reservation = new JobReservation(dbOptions, () => now);
            await using var scheduler = new JobScheduler(dbOptions, reservation, fakeExecutor, clock: () => now);
            var settingsService = new SettingsService(dbOptions, rootRegistry);
            var snapshotFactory = new JobSnapshotFactory(settingsService);
            var jobService = new JobService(dbOptions, snapshotFactory, scheduler, clock: () => now);

            // Người dùng gọi JobService.CancelAsync
            await jobService.CancelAsync(wsId, jobId);

            var reconcileService = new JobReconciliationService(dbOptions, clock: () => now);

            // KHẲNG ĐỊNH 1: Chặn retry trên tác vụ đã hủy (kể cả khi ConfirmedNotReceived = true)
            bool retryBlocked = false;
            try
            {
                await reconcileService.ReconcileAsync(new JobReconcileRequest(
                    WorkspaceId: wsId,
                    JobId: jobId,
                    Action: "retry",
                    ConfirmedNotReceived: true
                ));
            }
            catch (JobValidationException)
            {
                retryBlocked = true;
            }
            if (!retryBlocked)
            {
                throw new Exception("Reconcile retry trên tác vụ đã Canceled BẮT BUỘC phải bị chặn với JobValidationException!");
            }

            // KHẲNG ĐỊNH 2: Chặn resubmit trên tác vụ đã hủy (kể cả khi ConfirmedNotReceived = true)
            bool resubmitBlocked = false;
            try
            {
                await reconcileService.ReconcileAsync(new JobReconcileRequest(
                    WorkspaceId: wsId,
                    JobId: jobId,
                    Action: "resubmit",
                    ConfirmedNotReceived: true
                ));
            }
            catch (JobValidationException)
            {
                resubmitBlocked = true;
            }
            if (!resubmitBlocked)
            {
                throw new Exception("Reconcile resubmit trên tác vụ đã Canceled BẮT BUỘC phải bị chặn với JobValidationException!");
            }

            // KHẲNG ĐỊNH 3: Chặn finalize khi CHƯA có checkpoint
            bool finalizeWithoutCheckpointBlocked = false;
            try
            {
                await reconcileService.ReconcileAsync(new JobReconcileRequest(
                    WorkspaceId: wsId,
                    JobId: jobId,
                    Action: "finalize"
                ));
            }
            catch (JobValidationException)
            {
                finalizeWithoutCheckpointBlocked = true;
            }
            if (!finalizeWithoutCheckpointBlocked)
            {
                throw new Exception("Reconcile finalize trên tác vụ đã Canceled khi chưa có checkpoint BẮT BUỘC phải bị chặn!");
            }

            // Thiết lập checkpoint kết quả trung gian trong DB (giả lập AI đã sinh xong nhưng lỗi storage)
            const string checkpointPayload = "Payload checkpoint thu hoi tu AI sau cancel";
            var assetId = Guid.NewGuid().ToString("N");
            var relPath = $"{wsId}/images/{assetId}.png";
            fakeExecutor.FinalizeRelativePath = relPath;
            fakeExecutor.ResultReferenceToReturn = assetId;

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                db.Assets.Add(new Asset
                {
                    Id = assetId,
                    WorkspaceId = wsId,
                    ProjectId = prjId,
                    AssetType = "Image",
                    StorageRootId = "root_default",
                    RelativePath = relPath,
                    ByteSize = 1024,
                    Sha256Hash = "hash123",
                    Status = "Linked",
                    CreatedAtUtc = nowIso
                });

                var j = await db.Jobs.FirstAsync(x => x.Id == jobId);
                j.ResultReference = assetId;
                j.ResultText = checkpointPayload;
                await db.SaveChangesAsync();
            }

            var initialAiSubmits = fakeExecutor.TotalAiSubmits;

            // Gọi Reconcile finalize khi ĐÃ CÓ checkpoint
            var finalizeRes = await reconcileService.ReconcileAsync(new JobReconcileRequest(
                WorkspaceId: wsId,
                JobId: jobId,
                Action: "finalize"
            ));

            if (finalizeRes.CurrentState != JobState.Queued)
            {
                throw new Exception($"Reconcile finalize checkpoint phải đưa job về Queued để scheduler finalize! Thực tế: {finalizeRes.CurrentState}");
            }

            // Scheduler dispatch finalize output
            var dispatched = await scheduler.DispatchNextSingleJobAsync();
            if (!dispatched)
            {
                throw new Exception("Scheduler phải dispatch được lượt finalize của checkpoint!");
            }

            // Đợi scheduler xử lý xong
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Job? finalizedJob = null;
            while (sw.Elapsed < TimeSpan.FromSeconds(5))
            {
                await using var db = new TiToAIDbContext(dbOptions);
                var j = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == jobId);
                if (j != null && (j.State == JobState.Canceled || j.State == JobState.Succeeded))
                {
                    finalizedJob = j;
                    break;
                }
                await Task.Delay(50);
            }

            if (finalizedJob == null)
            {
                throw new TimeoutException("Hết thời gian chờ scheduler finalize checkpoint.");
            }

            // KHẲNG ĐỊNH 4: KHÔNG TĂNG AI submits (TotalAiSubmits giữ nguyên)
            if (fakeExecutor.TotalAiSubmits != initialAiSubmits)
            {
                throw new Exception($"Finalize checkpoint của tác vụ đã hủy TUYỆT ĐỐI KHÔNG được gọi AI sinh lại! Ban đầu: {initialAiSubmits}, hiện tại: {fakeExecutor.TotalAiSubmits}");
            }

            // KHẲNG ĐỊNH 5: Kiểm tra kết quả trong DB: giữ Canceled, UserCanceled, Asset Unlinked, Scene không bị áp
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var j = await db.Jobs.FirstAsync(x => x.Id == jobId);
                if (j.State != JobState.Canceled)
                {
                    throw new Exception($"Job sau khi finalize checkpoint của lệnh hủy phải ở Canceled! Thực tế: {j.State}");
                }
                if (j.ReasonCode != JobReasonCode.UserCanceled)
                {
                    throw new Exception($"Job reasonCode phải là user_canceled! Thực tế: {j.ReasonCode}");
                }

                // Asset được tạo và có Status = Unlinked
                if (string.IsNullOrWhiteSpace(j.ResultReference))
                {
                    throw new Exception("Job ResultReference phải được cập nhật AssetId!");
                }
                var asset = await db.Assets.FirstAsync(a => a.Id == j.ResultReference);
                if (asset.Status != "Unlinked")
                {
                    throw new Exception($"Asset phải có Status = 'Unlinked'! Thực tế: {asset.Status}");
                }

                // File thật đã được ghi trong storage
                var fullFilePath = Path.Combine(rootDir, asset.RelativePath);
                if (!File.Exists(fullFilePath))
                {
                    throw new Exception($"File kết quả phải được AssetFileStore ghi thật trong storage tại {fullFilePath}");
                }

                // Scene không bị áp Asset
                var scn = await db.Scenes.FirstAsync(s => s.Id == scnId);
                if (scn.ImageAssetId != null)
                {
                    throw new Exception("Scene TUYỆT ĐỐI KHÔNG được áp Asset khi Job đã bị hủy!");
                }

                // Occupancy đã được giải phóng
                var occ = await db.ActiveSlotOccupancies.FirstOrDefaultAsync(o => o.JobId == jobId);
                if (occ != null)
                {
                    throw new Exception("ActiveSlotOccupancy phải được giải phóng sau khi finalize checkpoint của tác vụ hủy!");
                }
            }
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// Chạy toàn bộ 12 bài test khói Crash Recovery & Reconcile Engine.
    /// </summary>
    public static async Task<int> RunAllAsync()
    {
        Console.WriteLine("=================================================================");
        Console.WriteLine("     TiToAI — Job Crash Recovery & Reconcile Smoke Checks (T16)  ");
        Console.WriteLine("=================================================================");

        int passed = 0;
        int failed = 0;

        async Task RunCheckAsync(string testName, Func<Task> testAction)
        {
            Console.Write($"[RUN] {testName} ... ");
            try
            {
                await testAction();
                passed++;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("PASS");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                failed++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("FAIL");
                Console.ResetColor();
                Console.WriteLine($"      -> Lỗi: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine($"      -> Stack: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"      -> Inner: {ex.InnerException.Message}");
                }
            }
        }

        await RunCheckAsync(
            "Test 1: Crash before side-effect recovers to Queued with exponential retry_after and deletes occupancy",
            Test_CrashBeforeSideEffect_RecoversToQueuedWithRetryAfterAndDeletesOccupancy);

        await RunCheckAsync(
            "Test 2: Crash before side-effect with max attempts recovers to Failed and deletes occupancy",
            Test_CrashBeforeSideEffect_MaxAttemptsExceeded_RecoversToFailedAndDeletesOccupancy);

        await RunCheckAsync(
            "Test 3: Crash after side-effect recovers to AwaitingUser and PRESERVES occupancy",
            Test_CrashAfterSideEffect_RecoversToAwaitingUserAndPreservesOccupancy);

        await RunCheckAsync(
            "Test 4: Concurrent recovery sweeps have exactly one winner and no corruption",
            Test_ConcurrentRecoverySweeps_ExactlyOneWinnerNoCorruption);

        await RunCheckAsync(
            "Test 5: Reconcile resubmit requires ConfirmedNotReceived when side-effect submitted",
            Test_ReconcileResubmit_RequiresConfirmedNotReceivedWhenSideEffectSubmitted);

        await RunCheckAsync(
            "Test 6: Reconcile mark_succeeded requires valid result and releases occupancy",
            Test_ReconcileMarkSucceeded_RequiresValidResultAndReleasesOccupancy);

        await RunCheckAsync(
            "Test 7: Reconcile mark_failed and cancel work properly and release occupancy",
            Test_ReconcileMarkFailedAndCancel_WorksProperlyAndReleasesOccupancy);

        await RunCheckAsync(
            "Test 8: Restart startup recovery preserves ExecutionBinding, snapshots, and occupancy",
            Test_RestartStartupRecovery_PreservesBindingAndSnapshots);

        await RunCheckAsync(
            "Test 9: Capacity 1 cancel unresolved side-effect preserves occupancy and blocks new job until safe reconcile",
            Test9_Capacity1_Submit_LeaseExpired_AwaitingUser_Cancel_BlocksNewJobUntilReconciled);

        await RunCheckAsync(
            "Test 10: Late result reconcile after cancel keeps Canceled, unlinks asset, and does not apply to scene",
            Test10_LateResultAfterCancel_KeepsCanceled_UnlinksAsset_DoesNotApplyToScene);

        await RunCheckAsync(
            "Test 11: Canceled unresolved job listed in list_active, removed after reconcile and slot freed",
            Test11_CanceledUnresolvedJob_ListedInListActive_RemovedAfterReconcileAndSlotFreed);

        await RunCheckAsync(
            "Test 12: Canceled job retry/resubmit blocked, finalize preserves canceled intent without new AI submit",
            Test12_CanceledJob_ReconcileRetryAndResubmitBlocked_FinalizePreservesCanceledIntent);

        Console.WriteLine("=================================================================");
        if (failed == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"    ALL CRASH RECOVERY SMOKE CHECKS PASSED ({passed}/{passed})       ");
            Console.ResetColor();
            Console.WriteLine("=================================================================");
            return 0;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"    CRASH RECOVERY SMOKE CHECKS FAILED: {failed} failed, {passed} passed");
            Console.ResetColor();
            Console.WriteLine("=================================================================");
            return 1;
        }
    }
}
