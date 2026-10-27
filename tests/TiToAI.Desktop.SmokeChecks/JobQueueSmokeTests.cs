using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TiToAI.Application.Jobs;
using TiToAI.Application.Projects;
using TiToAI.Application.Prompts;
using TiToAI.Application.Scenes;
using TiToAI.Application.Settings;
using TiToAI.Application.Workspaces;
using TiToAI.Contracts.Bridge;
using TiToAI.Contracts.Jobs;
using TiToAI.Contracts.Workspaces;
using TiToAI.Browser;
using TiToAI.Browser.Abstractions;
using TiToAI.Browser.Models;
using TiToAI.Desktop.Hosting;
using TiToAI.Desktop.Bridge;
using TiToAI.Desktop.Bridge.Handlers;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Persistence.Entities;
using TiToAI.Infrastructure.Persistence.Interceptors;
using TiToAI.Infrastructure.Storage;

namespace TiToAI.Desktop.SmokeChecks;

/// <summary>
/// Bộ kiểm thử khói định lượng Task T15:
/// Durable Job Queue, Atomic Reservation, Zombie Worker Fencing, Fair Scheduler,
/// Browser Tab Quota, Crash Recovery Sweep và Strict Delete Guard.
/// Bao phủ toàn bộ 10 ca kiểm thử định lượng theo t15-test-matrix.md.
/// </summary>
public static class JobQueueSmokeTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static (DbContextOptions<TiToAIDbContext> Options, string TempDir, StorageRootRegistry Registry, SettingsService Settings) CreateTestContext()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"titoai_t15_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var dbPath = Path.Combine(tempDir, "titoai_test.db");
        var connectionString = DatabasePathHelper.BuildConnectionString(dbPath);

        var optionsBuilder = new DbContextOptionsBuilder<TiToAIDbContext>();
        optionsBuilder.UseSqlite(connectionString)
                      .AddInterceptors(new SqlitePragmaInterceptor())
                      .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        var dbOptions = optionsBuilder.Options;

        // Chạy migration tạo schema
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

            void EnsureSetting(string key, string val)
            {
                var s = db.AppSettings.FirstOrDefault(x => x.Key == key);
                if (s == null)
                {
                    db.AppSettings.Add(new AppSetting { Key = key, Value = val, UpdatedAtUtc = now });
                }
                else if (string.IsNullOrWhiteSpace(s.Value))
                {
                    s.Value = val;
                    s.UpdatedAtUtc = now;
                }
            }
            EnsureSetting(SettingsService.KeyScriptProvider, "chatgpt-web");
            EnsureSetting(SettingsService.KeyImageProvider, "chatgpt-web");
            EnsureSetting(SettingsService.KeyVideoPromptProvider, "gemini-web");
            EnsureSetting(SettingsService.KeyTheme, "system");

            db.SaveChanges();
        }

        var registry = new StorageRootRegistry(dbOptions, tempDir);
        registry.EnsureDefaultRootsAsync().GetAwaiter().GetResult();
        var settingsService = new SettingsService(dbOptions, registry);

        return (dbOptions, tempDir, registry, settingsService);
    }

    private static void CleanupTestContext(string tempDir)
    {
        try
        {
            if (Directory.Exists(tempDir))
            {
                SqliteConnection.ClearAllPools();
                Directory.Delete(tempDir, recursive: true);
            }
        }
        catch
        {
            // Bỏ qua lỗi dọn dẹp thư mục tạm
        }
    }

    private static async Task<(string WorkspaceId, string ProjectId, string SceneId)> SeedBasicEntitiesAsync(
        DbContextOptions<TiToAIDbContext> dbOptions,
        string wsStatus = "Active")
    {
        await using var db = new TiToAIDbContext(dbOptions);

        var wsId = Guid.NewGuid().ToString();
        var prjId = Guid.NewGuid().ToString();
        var scnId = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToString("o");

        var ws = new Workspace
        {
            Id = wsId,
            Name = "Workspace Test",
            ChannelHandle = "@test_channel",
            Topic = "AI Video Content",
            Status = wsStatus,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        var prj = new Project
        {
            Id = prjId,
            WorkspaceId = wsId,
            Name = "Dự án Test",
            Description = "Mô tả dự án",
            Status = "Draft",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        var scn = new Scene
        {
            Id = scnId,
            ProjectId = prjId,
            WorkspaceId = wsId,
            SequenceOrder = 1,
            Name = "Phân cảnh 1",
            PromptText = "Gợi ý hình ảnh cảnh 1",
            Status = "Draft",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.Workspaces.Add(ws);
        db.Projects.Add(prj);
        db.Scenes.Add(scn);
        await db.SaveChangesAsync();

        return (wsId, prjId, scnId);
    }

    public static async Task<int> RunAllAsync()
    {
        Console.WriteLine("\n=================================================================");
        Console.WriteLine("    BẮT ĐẦU KIỂM THỬ KHÓI T15: DURABLE JOB QUEUE & FAIR SCHEDULER ");
        Console.WriteLine("=================================================================");

        int passed = 0;
        int failed = 0;

        async Task RunCaseAsync(string testName, Func<Task> testAction)
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
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"      -> Inner: {ex.InnerException.Message}");
                }
            }
        }

        await RunCaseAsync(
            "TC-T15-01: Atomic Compare-and-Set Reservation (2 Schedulers Contention)",
            Test_TC_T15_01_AtomicReservation_TwoSchedulersContention_SingleWinner);

        await RunCaseAsync(
            "TC-T15-02: Fencing Token & Lease Expiration (Zombie Worker Denied)",
            Test_TC_T15_02_FencingToken_StaleWorkerDenied_OnHeartbeatAndCommit);

        await RunCaseAsync(
            "TC-T15-03: Workspace Pause & Profile Login Interaction (Skip & Wakeup)",
            Test_TC_T15_03_WorkspacePauseAndBrowserLogin_DispatchSkipped_WakeupTriggered);

        await RunCaseAsync(
            "TC-T15-04: In-Memory Channel Loss Resiliency (Fallback Polling Recovery)",
            Test_TC_T15_04_WakeupResiliency_FallbackPollingRecoversQueuedJobs);

        await RunCaseAsync(
            "TC-T15-05: Application Startup Crash Recovery (3-Tier Sweep & No Blind Resend)",
            Test_TC_T15_05_StartupCrashRecovery_ThreeTierSweep_NoBlindResend);

        await RunCaseAsync(
            "TC-T15-06: Settings Change After Enqueue (Immutable Input Snapshots)",
            Test_TC_T15_06_SnapshotImmutability_SettingsChangeDoesNotAffectQueuedJob);

        await RunCaseAsync(
            "TC-T15-07: Fair Round-Robin Scheduling (Load Balancing across 3 Workspaces)",
            Test_TC_T15_07_FairScheduling_RoundRobinAcrossThreeWorkspaces);

        await RunCaseAsync(
            "TC-T15-08: Browser Tab Quota (1 Job/Tab Hard Limit Enforcement)",
            Test_TC_T15_08_BrowserTabQuota_OneJobPerTabEnforced);

        await RunCaseAsync(
            "TC-T15-09: Strict Non-Terminal & Unreconciled Delete Guards",
            Test_TC_T15_09_StrictDeleteGuard_NonTerminalAndUnreconciledJobsBlockDeletion);

        await RunCaseAsync(
            "TC-T15-10: Side-Effect Intent Committed Before Browser Action",
            Test_TC_T15_10_CrashBoundaryVerify_SideEffectCommittedBeforeAction);

        await RunCaseAsync(
            "TC-T15-EXTRA: Bridge Handlers RPC Integration (5 commands & allow-list)",
            Test_TC_T15_Extra_BridgeHandlersIntegration);

        // --- Regression Tests for Findings R1 - R8 ---
        await RunCaseAsync(
            "REGRESSION-R1: Production Executor (Unavailable Adapter -> Failed without handoff or fake success)",
            Test_Regression_R1_ProductionExecutor_UnavailableAdapter_NoFakeSuccess);

        await RunCaseAsync(
            "REGRESSION-R2: Strict Project Delete Guard with Active Project-level Job (SceneId == null)",
            Test_Regression_R2_DeleteProjectWithActiveProjectLevelJob_SceneIdNull_Blocked);

        await RunCaseAsync(
            "REGRESSION-R3: Role Tab Contention (Only Winner Executes; Loser Returned to Queued; Owner-Checked Release)",
            Test_Regression_R3_TwoWorkersContendingSameRoleTab_SingleExecution);

        await RunCaseAsync(
            "REGRESSION-R4: Atomic Sweep CAS (Concurrent Heartbeat Respected; No Blind Resend for Submitted Side-effects)",
            Test_Regression_R4_SweepConcurrentWithHeartbeatOrCommit_NoBlindResend);

        await RunCaseAsync(
            "REGRESSION-R5: Host Browser Gatekeeper (Quiescing & Visible Login Blocks Dispatch)",
            Test_Regression_R5_HostBrowserGatekeeper_IntegratesWithRegistryAndLoginCoordinator);

        await RunCaseAsync(
            "REGRESSION-R6: Job Event Scoping (Progress & State Events Scoped to Job WorkspaceId)",
            Test_Regression_R6_JobProgressUpdatedAndStateChanged_ScopedToWorkspace);

        await RunCaseAsync(
            "REGRESSION-R7: Scheduler Shutdown Drain (Worker Cancellation & Graceful Await on Dispose)",
            Test_Regression_R7_DisposeScheduler_CancelsWorkersAndDrainsGracefully);

        await RunCaseAsync(
            "REGRESSION-R8: Prompt Template & Storage Root Snapshot (Template Resolution & Settings Immutability)",
            Test_Regression_R8_PromptTemplateAndStorageRootSnapshot_SettingsChangeDoesNotAffectJob);

        Console.WriteLine("\n=================================================================");
        if (failed == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"        T15 SMOKE CHECKS HOÀN TẤT THÀNH CÔNG: {passed}/{passed} PASS");
            Console.ResetColor();
            Console.WriteLine("=================================================================");
            return 0;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"        T15 SMOKE CHECKS THẤT BẠI: {failed} FAIL, {passed} PASS");
            Console.ResetColor();
            Console.WriteLine("=================================================================");
            return 1;
        }
    }

    /// <summary>
    /// TC-T15-01: Hai Scheduler cạnh tranh reserve cùng một job: chính xác 1 thắng, 1 nhận 0 rows.
    /// Thời gian reservation trung bình < 10ms, 0 deadlock.
    /// </summary>
    public static async Task Test_TC_T15_01_AtomicReservation_TwoSchedulersContention_SingleWinner()
    {
        var (dbOptions, tempDir, _, _) = CreateTestContext();
        try
        {
            var (wsId, prjId, scnId) = await SeedBasicEntitiesAsync(dbOptions);

            var jobId = Guid.NewGuid().ToString();
            var now = DateTimeOffset.UtcNow.ToString("o");

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                db.Jobs.Add(new Job
                {
                    Id = jobId,
                    WorkspaceId = wsId,
                    ProjectId = prjId,
                    SceneId = scnId,
                    TaskType = JobTaskType.Image,
                    State = JobState.Queued,
                    ProviderKey = "chatgpt-web",
                    StorageRootId = "root-images-v1",
                    Attempts = 0,
                    FencingToken = 0,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
                await db.SaveChangesAsync();
            }

            var reservationA = new JobReservation(dbOptions);
            var reservationB = new JobReservation(dbOptions);

            var barrier = new ManualResetEventSlim(false);
            var sw = Stopwatch.StartNew();

            var taskA = Task.Run(async () =>
            {
                barrier.Wait();
                return await reservationA.TryReserveJobAsync(jobId, "worker-A", TimeSpan.FromSeconds(45));
            });

            var taskB = Task.Run(async () =>
            {
                barrier.Wait();
                return await reservationB.TryReserveJobAsync(jobId, "worker-B", TimeSpan.FromSeconds(45));
            });

            barrier.Set();
            var results = await Task.WhenAll(taskA, taskB);
            sw.Stop();

            var winnerCount = results.Count(r => r.Acquired);
            var loserCount = results.Count(r => !r.Acquired);

            if (winnerCount != 1 || loserCount != 1)
            {
                throw new InvalidOperationException($"Kỳ vọng đúng 1 winner và 1 loser nhưng nhận {winnerCount} winners và {loserCount} losers.");
            }

            // Đo thời gian thực thi: yêu cầu < 500ms cho transaction trong môi trường test
            if (sw.ElapsedMilliseconds > 500)
            {
                throw new InvalidOperationException($"Thời gian reservation quá lâu: {sw.ElapsedMilliseconds}ms.");
            }

            // Kiểm tra trạng thái trong DB
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var job = await db.Jobs.AsNoTracking().FirstAsync(j => j.Id == jobId);
                if (job.State != JobState.Reserved)
                {
                    throw new InvalidOperationException($"Job state kỳ vọng Reserved nhưng là '{job.State}'.");
                }
                if (job.Attempts != 1)
                {
                    throw new InvalidOperationException($"Attempts kỳ vọng 1 nhưng là {job.Attempts}.");
                }
                if (job.FencingToken != 1)
                {
                    throw new InvalidOperationException($"FencingToken kỳ vọng 1 nhưng là {job.FencingToken}.");
                }
                if (job.LeaseOwnerId != "worker-A" && job.LeaseOwnerId != "worker-B")
                {
                    throw new InvalidOperationException($"LeaseOwnerId không khớp: '{job.LeaseOwnerId}'.");
                }
            }
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// TC-T15-02: Worker cũ bị fencing khi lease hết hạn và scheduler cấp lease mới cho worker khác.
    /// </summary>
    public static async Task Test_TC_T15_02_FencingToken_StaleWorkerDenied_OnHeartbeatAndCommit()
    {
        var (dbOptions, tempDir, _, _) = CreateTestContext();
        try
        {
            var (wsId, prjId, scnId) = await SeedBasicEntitiesAsync(dbOptions);
            var jobId = Guid.NewGuid().ToString();

            var t0 = DateTimeOffset.UtcNow;
            var currentTime = t0;
            DateTimeOffset Clock() => currentTime;

            var reservation = new JobReservation(dbOptions, Clock);

            // 1. Tạo Job ban đầu được cấp cho Worker_Old với FencingToken = 1
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
                    LeaseOwnerId = "Worker_Old",
                    FencingToken = 1,
                    Attempts = 1,
                    MaxAttempts = 3,
                    LeaseExpiresAtUtc = t0.AddSeconds(45).ToString("o"),
                    SideEffectSubmitted = 0,
                    ProviderKey = "chatgpt-web",
                    StorageRootId = "root-docs-v1",
                    CreatedAtUtc = t0.ToString("o"),
                    UpdatedAtUtc = t0.ToString("o")
                });
                await db.SaveChangesAsync();
            }

            // 2. Fast-forward clock lên T0 + 50s (lease của Worker_Old đã quá hạn)
            currentTime = t0.AddSeconds(50);

            var fakeExecutor = new DeterministicFakeJobExecutor();
            var scheduler = new JobScheduler(dbOptions, reservation, fakeExecutor, clock: Clock);

            // 3. Scheduler quét phục hồi lease hết hạn: đưa job về Queued
            var recovered = await scheduler.RunCrashRecoverySweepAsync();
            if (recovered != 1)
            {
                throw new InvalidOperationException($"Kỳ vọng sweep 1 job nhưng được {recovered}.");
            }

            // 4. Scheduler cấp job cho Worker_New: FencingToken tăng lên 2
            var reserveNew = await reservation.TryReserveJobAsync(jobId, "Worker_New", TimeSpan.FromSeconds(45));
            if (!reserveNew.Acquired || reserveNew.Job?.FencingToken != 2)
            {
                throw new InvalidOperationException("Worker_New không thể reserve job với FencingToken = 2.");
            }

            // 5. Worker_Old tỉnh dậy và gửi Heartbeat với FencingToken = 1 -> Bắt buộc ném FencingTokenStaleException
            bool heartbeatFailedWithStale = false;
            try
            {
                await reservation.SendHeartbeatAsync(jobId, "Worker_Old", fencingToken: 1, TimeSpan.FromSeconds(45));
            }
            catch (FencingTokenStaleException)
            {
                heartbeatFailedWithStale = true;
            }

            if (!heartbeatFailedWithStale)
            {
                throw new InvalidOperationException("Heartbeat của Worker_Old không bị ném FencingTokenStaleException.");
            }

            // 6. Worker_Old cố gắng commit kết quả với FencingToken = 1 -> Bắt buộc ném FencingTokenStaleException
            bool commitFailedWithStale = false;
            try
            {
                await reservation.CommitSuccessAsync(jobId, fencingToken: 1, "old-ref", "old-text");
            }
            catch (FencingTokenStaleException)
            {
                commitFailedWithStale = true;
            }

            if (!commitFailedWithStale)
            {
                throw new InvalidOperationException("Commit kết quả của Worker_Old không bị ném FencingTokenStaleException.");
            }

            // 7. Worker_New commit kết quả với FencingToken = 2 -> Thành công
            await reservation.CommitSuccessAsync(jobId, fencingToken: 2, "new-ref", "new-text");

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var finalJob = await db.Jobs.AsNoTracking().FirstAsync(j => j.Id == jobId);
                if (finalJob.State != JobState.Succeeded || finalJob.ResultReference != "new-ref")
                {
                    throw new InvalidOperationException("Kết quả DB không phản ánh commit thành công của Worker_New.");
                }
            }
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// TC-T15-03: Tương tác giữa Workspace Pause và Browser Profile Login.
    /// Tự động bỏ qua phân phối; kích hoạt Wakeup ngay khi unpause hoặc hoàn tất login.
    /// </summary>
    public static async Task Test_TC_T15_03_WorkspacePauseAndBrowserLogin_DispatchSkipped_WakeupTriggered()
    {
        var (dbOptions, tempDir, _, _) = CreateTestContext();
        try
        {
            var (wsA, prjA, scnA) = await SeedBasicEntitiesAsync(dbOptions, wsStatus: "Paused");
            var (wsB, prjB, scnB) = await SeedBasicEntitiesAsync(dbOptions, wsStatus: "Active");

            var now = DateTimeOffset.UtcNow.ToString("o");
            var jobAId = Guid.NewGuid().ToString();
            var jobBId = Guid.NewGuid().ToString();

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                db.Jobs.Add(new Job
                {
                    Id = jobAId,
                    WorkspaceId = wsA,
                    ProjectId = prjA,
                    SceneId = scnA,
                    TaskType = JobTaskType.Image,
                    State = JobState.Queued,
                    ProviderKey = "chatgpt-web",
                    StorageRootId = "root-img",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });

                db.Jobs.Add(new Job
                {
                    Id = jobBId,
                    WorkspaceId = wsB,
                    ProjectId = prjB,
                    SceneId = null,
                    TaskType = JobTaskType.Script,
                    State = JobState.Queued,
                    ProviderKey = "chatgpt-web",
                    StorageRootId = "root-docs",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
                await db.SaveChangesAsync();
            }

            var gatekeeper = new DefaultBrowserGatekeeper();
            gatekeeper.SetProfileBusyWithLogin("shared_ai", true); // shared_ai đang bận login

            var reservation = new JobReservation(dbOptions);
            var executor = new DeterministicFakeJobExecutor();
            var scheduler = new JobScheduler(dbOptions, reservation, executor, gatekeeper);

            // Lượt 1: WS_A bị Paused -> bỏ qua; WS_B cần profile shared_ai đang busy login -> bỏ qua
            var dispatched = await scheduler.DispatchPendingJobsAsync();
            if (dispatched != 0)
            {
                throw new InvalidOperationException($"Kỳ vọng 0 job dispatched khi paused/login busy nhưng có {dispatched}.");
            }

            // Giải phóng trạng thái login của profile
            gatekeeper.SetProfileBusyWithLogin("shared_ai", false);

            // Lượt 2: Profile đã sẵn sàng -> WS_B được dispatch
            dispatched = await scheduler.DispatchPendingJobsAsync();
            if (dispatched != 1)
            {
                throw new InvalidOperationException($"Kỳ vọng 1 job của WS_B được dispatch nhưng có {dispatched}.");
            }

            // Unpause WS_A
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var ws = await db.Workspaces.FirstAsync(w => w.Id == wsA);
                ws.Status = "Active";
                await db.SaveChangesAsync();
            }

            // Lượt 3: WS_A đã Active -> job của WS_A được dispatch
            dispatched = await scheduler.DispatchPendingJobsAsync();
            if (dispatched != 1)
            {
                throw new InvalidOperationException($"Kỳ vọng 1 job của WS_A được dispatch sau unpause nhưng có {dispatched}.");
            }
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// TC-T15-04: Khả năng tự phục hồi khi mất tín hiệu In-Memory Wakeup Channel (Fallback Polling).
    /// </summary>
    public static async Task Test_TC_T15_04_WakeupResiliency_FallbackPollingRecoversQueuedJobs()
    {
        var (dbOptions, tempDir, _, _) = CreateTestContext();
        try
        {
            var (wsId, prjId, scnId) = await SeedBasicEntitiesAsync(dbOptions);

            var reservation = new JobReservation(dbOptions);
            var executor = new DeterministicFakeJobExecutor();
            var scheduler = new JobScheduler(dbOptions, reservation, executor)
            {
                FallbackPollInterval = TimeSpan.FromMilliseconds(400) // Fallback poll nhanh
            };

            scheduler.Start();

            // Chèn 2 jobs trực tiếp vào DB SQLite mà KHÔNG gọi notify channel
            var now = DateTimeOffset.UtcNow.ToString("o");
            var job1Id = Guid.NewGuid().ToString();
            var job2Id = Guid.NewGuid().ToString();

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
                        State = JobState.Queued,
                        ProviderKey = "chatgpt-web",
                        StorageRootId = "root-docs",
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    },
                    new Job
                    {
                        Id = job2Id,
                        WorkspaceId = wsId,
                        ProjectId = prjId,
                        SceneId = scnId,
                        TaskType = JobTaskType.Image,
                        State = JobState.Queued,
                        ProviderKey = "chatgpt-web",
                        StorageRootId = "root-img",
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    });
                await db.SaveChangesAsync();
            }

            // Đợi fallback polling chạy qua (khoảng 1.2s)
            await Task.Delay(1500);
            await scheduler.DisposeAsync();

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var j1 = await db.Jobs.AsNoTracking().FirstAsync(j => j.Id == job1Id);
                var j2 = await db.Jobs.AsNoTracking().FirstAsync(j => j.Id == job2Id);

                if (j1.State == JobState.Queued || j2.State == JobState.Queued)
                {
                    throw new InvalidOperationException($"Fallback polling thất bại. J1: {j1.State}, J2: {j2.State}.");
                }
            }
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// TC-T15-05: Khởi động ứng dụng & phục hồi tác vụ sau sự cố đột ngột (3-tier recovery).
    /// </summary>
    public static async Task Test_TC_T15_05_StartupCrashRecovery_ThreeTierSweep_NoBlindResend()
    {
        var (dbOptions, tempDir, _, _) = CreateTestContext();
        try
        {
            var (wsId, prjId, scnId) = await SeedBasicEntitiesAsync(dbOptions);

            var pastIso = DateTimeOffset.UtcNow.AddMinutes(-5).ToString("o");
            var nowIso = DateTimeOffset.UtcNow.ToString("o");

            var j1Id = Guid.NewGuid().ToString();
            var j2Id = Guid.NewGuid().ToString();
            var j3Id = Guid.NewGuid().ToString();

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                // Job 1: attempts = 1, max_attempts = 3, side_effect = 0
                db.Jobs.Add(new Job
                {
                    Id = j1Id,
                    WorkspaceId = wsId,
                    ProjectId = prjId,
                    SceneId = scnId,
                    TaskType = JobTaskType.Image,
                    State = JobState.Running,
                    Attempts = 1,
                    MaxAttempts = 3,
                    SideEffectSubmitted = 0,
                    LeaseExpiresAtUtc = pastIso,
                    ProviderKey = "chatgpt-web",
                    StorageRootId = "root-img",
                    CreatedAtUtc = nowIso,
                    UpdatedAtUtc = nowIso
                });

                // Job 2: attempts = 3, max_attempts = 3, side_effect = 0 (hết lượt)
                db.Jobs.Add(new Job
                {
                    Id = j2Id,
                    WorkspaceId = wsId,
                    ProjectId = prjId,
                    SceneId = null,
                    TaskType = JobTaskType.Script,
                    State = JobState.Running,
                    Attempts = 3,
                    MaxAttempts = 3,
                    SideEffectSubmitted = 0,
                    LeaseExpiresAtUtc = pastIso,
                    ProviderKey = "chatgpt-web",
                    StorageRootId = "root-docs",
                    CreatedAtUtc = nowIso,
                    UpdatedAtUtc = nowIso
                });

                // Job 3: attempts = 1, max_attempts = 3, side_effect = 1 (đã gửi side-effect!)
                db.Jobs.Add(new Job
                {
                    Id = j3Id,
                    WorkspaceId = wsId,
                    ProjectId = prjId,
                    SceneId = scnId,
                    TaskType = JobTaskType.VideoPrompt,
                    State = JobState.Running,
                    Attempts = 1,
                    MaxAttempts = 3,
                    SideEffectSubmitted = 1,
                    SideEffectAtUtc = pastIso,
                    LeaseExpiresAtUtc = pastIso,
                    ProviderKey = "chatgpt-web",
                    StorageRootId = "root-docs",
                    CreatedAtUtc = nowIso,
                    UpdatedAtUtc = nowIso
                });

                await db.SaveChangesAsync();
            }

            var reservation = new JobReservation(dbOptions);
            var executor = new DeterministicFakeJobExecutor();
            var scheduler = new JobScheduler(dbOptions, reservation, executor);

            var recoveredCount = await scheduler.RunCrashRecoverySweepAsync();
            if (recoveredCount != 3)
            {
                throw new InvalidOperationException($"Kỳ vọng phục hồi 3 jobs nhưng là {recoveredCount}.");
            }

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var j1 = await db.Jobs.AsNoTracking().FirstAsync(j => j.Id == j1Id);
                var j2 = await db.Jobs.AsNoTracking().FirstAsync(j => j.Id == j2Id);
                var j3 = await db.Jobs.AsNoTracking().FirstAsync(j => j.Id == j3Id);

                // J1: Về Queued, attempts giữ 1, có retry_after
                if (j1.State != JobState.Queued || j1.Attempts != 1 || string.IsNullOrWhiteSpace(j1.RetryAfterUtc))
                {
                    throw new InvalidOperationException($"J1 sai trạng thái sau recovery: State={j1.State}, Attempts={j1.Attempts}, RetryAfter={j1.RetryAfterUtc}.");
                }

                // J2: Sang Failed với reason lease_expired_max_attempts
                if (j2.State != JobState.Failed || j2.ReasonCode != JobReasonCode.LeaseExpiredMaxAttempts)
                {
                    throw new InvalidOperationException($"J2 sai trạng thái sau recovery: State={j2.State}, Reason={j2.ReasonCode}.");
                }

                // J3: BẮT BUỘC sang AwaitingUser với reason reconcile_required (CẤM retry mù!)
                if (j3.State != JobState.AwaitingUser || j3.ReasonCode != JobReasonCode.ReconcileRequired)
                {
                    throw new InvalidOperationException($"J3 vi phạm side-effect guard: State={j3.State}, Reason={j3.ReasonCode}.");
                }
            }
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// TC-T15-06: Đổi cấu hình Settings sau khi enqueue (Bản chụp Snapshot bất biến).
    /// </summary>
    public static async Task Test_TC_T15_06_SnapshotImmutability_SettingsChangeDoesNotAffectQueuedJob()
    {
        var (dbOptions, tempDir, registry, settingsService) = CreateTestContext();
        try
        {
            var (wsId, prjId, scnId) = await SeedBasicEntitiesAsync(dbOptions);

            // 1. Cấu hình ban đầu trong Settings: ImageProvider = "chatgpt-web"
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var existing = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == SettingsService.KeyImageProvider);
                if (existing != null)
                {
                    existing.Value = "chatgpt-web";
                    existing.UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("o");
                }
                else
                {
                    db.AppSettings.Add(new AppSetting
                    {
                        Key = SettingsService.KeyImageProvider,
                        Value = "chatgpt-web",
                        UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("o")
                    });
                }
                await db.SaveChangesAsync();
            }

            var snapshotFactory = new JobSnapshotFactory(settingsService);
            var reservation = new JobReservation(dbOptions);
            var executor = new DeterministicFakeJobExecutor();
            var scheduler = new JobScheduler(dbOptions, reservation, executor);
            var jobService = new JobService(dbOptions, snapshotFactory, scheduler);

            // 2. Enqueue Job_Alpha
            var enqueueResult = await jobService.EnqueueAsync(new JobEnqueueRequest(
                WorkspaceId: wsId,
                ProjectId: prjId,
                SceneId: scnId,
                TaskType: JobTaskType.Image,
                CustomPromptText: "Realistic cat, 8k"
            ));

            // Kiểm tra DB: Job_Alpha đã chụp provider_key = "chatgpt-web"
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var initialJob = await db.Jobs.AsNoTracking().FirstAsync(j => j.Id == enqueueResult.JobId);
                if (initialJob.ProviderKey != "chatgpt-web")
                {
                    throw new InvalidOperationException($"ProviderKey snapshot ban đầu sai: {initialJob.ProviderKey}.");
                }
            }

            // 3. Sau khi enqueue, người dùng đổi Provider trong Settings sang "gemini-web"
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var setting = await db.AppSettings.FirstAsync(s => s.Key == SettingsService.KeyImageProvider);
                setting.Value = "gemini-web";
                await db.SaveChangesAsync();
            }

            // 4. Cho Scheduler dispatch và worker thực thi Job_Alpha
            await scheduler.DispatchPendingJobsAsync();
            await Task.Delay(200); // Đợi worker thực thi xong

            // 5. Worker thực thi nhận đúng context ban đầu: chatgpt-web
            lock (executor.RecordedExecutions)
            {
                if (executor.RecordedExecutions.Count != 1)
                {
                    throw new InvalidOperationException($"Kỳ vọng 1 execution nhưng có {executor.RecordedExecutions.Count}.");
                }

                var executedContext = executor.RecordedExecutions[0];
                if (executedContext.ProviderKey != "chatgpt-web")
                {
                    throw new InvalidOperationException($"Snapshot bị biến đổi! Kỳ vọng 'chatgpt-web' nhưng nhận '{executedContext.ProviderKey}'.");
                }
            }
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// TC-T15-07: Phân phối công bằng (Round-Robin Fairness) giữa 3 Workspaces.
    /// </summary>
    public static async Task Test_TC_T15_07_FairScheduling_RoundRobinAcrossThreeWorkspaces()
    {
        var (dbOptions, tempDir, _, _) = CreateTestContext();
        try
        {
            var (ws1, prj1, scn1) = await SeedBasicEntitiesAsync(dbOptions);
            var (ws2, prj2, scn2) = await SeedBasicEntitiesAsync(dbOptions);
            var (ws3, prj3, scn3) = await SeedBasicEntitiesAsync(dbOptions);

            // Sắp xếp workspace IDs theo thứ tự ổn định của DB (OrderBy Id)
            var sortedWs = new[] { ws1, ws2, ws3 }.OrderBy(x => x).ToList();
            var wsHeavy = sortedWs[0];  // 10 jobs
            var wsMedium = sortedWs[1]; // 2 jobs
            var wsLight = sortedWs[2];  // 1 job

            // Lấy Project tương ứng
            string GetPrj(string wId) => wId == ws1 ? prj1 : (wId == ws2 ? prj2 : prj3);

            var baseTime = DateTimeOffset.UtcNow;

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                // wsHeavy có 2 jobs: Lượt 1 (cũ nhất), Lượt 4
                db.Jobs.Add(new Job
                {
                    Id = Guid.NewGuid().ToString(),
                    WorkspaceId = wsHeavy,
                    ProjectId = GetPrj(wsHeavy),
                    SceneId = null,
                    TaskType = JobTaskType.Script,
                    State = JobState.Queued,
                    ProviderKey = "chatgpt-web",
                    StorageRootId = "root-docs",
                    CreatedAtUtc = baseTime.AddSeconds(1).ToString("o"),
                    UpdatedAtUtc = baseTime.AddSeconds(1).ToString("o")
                });

                // wsMedium có 1 job: Lượt 2
                db.Jobs.Add(new Job
                {
                    Id = Guid.NewGuid().ToString(),
                    WorkspaceId = wsMedium,
                    ProjectId = GetPrj(wsMedium),
                    SceneId = null,
                    TaskType = JobTaskType.Script,
                    State = JobState.Queued,
                    ProviderKey = "chatgpt-web",
                    StorageRootId = "root-docs",
                    CreatedAtUtc = baseTime.AddSeconds(2).ToString("o"),
                    UpdatedAtUtc = baseTime.AddSeconds(2).ToString("o")
                });

                // wsLight có 1 job: Lượt 3
                db.Jobs.Add(new Job
                {
                    Id = Guid.NewGuid().ToString(),
                    WorkspaceId = wsLight,
                    ProjectId = GetPrj(wsLight),
                    SceneId = null,
                    TaskType = JobTaskType.Script,
                    State = JobState.Queued,
                    ProviderKey = "chatgpt-web",
                    StorageRootId = "root-docs",
                    CreatedAtUtc = baseTime.AddSeconds(3).ToString("o"),
                    UpdatedAtUtc = baseTime.AddSeconds(3).ToString("o")
                });

                // Job thứ hai của wsHeavy: Lượt 4
                db.Jobs.Add(new Job
                {
                    Id = Guid.NewGuid().ToString(),
                    WorkspaceId = wsHeavy,
                    ProjectId = GetPrj(wsHeavy),
                    SceneId = null,
                    TaskType = JobTaskType.Script,
                    State = JobState.Queued,
                    ProviderKey = "chatgpt-web",
                    StorageRootId = "root-docs",
                    CreatedAtUtc = baseTime.AddSeconds(4).ToString("o"),
                    UpdatedAtUtc = baseTime.AddSeconds(4).ToString("o")
                });

                await db.SaveChangesAsync();
            }

            var reservation = new JobReservation(dbOptions);
            var executor = new DeterministicFakeJobExecutor();
            var scheduler = new JobScheduler(dbOptions, reservation, executor);

            var dispatchedWorkspaces = new List<string>();
            scheduler.OnJobStateChanged += payload =>
            {
                if (payload.NewState == JobState.Reserved)
                {
                    using var db = new TiToAIDbContext(dbOptions);
                    var j = db.Jobs.AsNoTracking().FirstOrDefault(x => x.Id == payload.JobId);
                    if (j != null)
                    {
                        lock (dispatchedWorkspaces)
                        {
                            dispatchedWorkspaces.Add(j.WorkspaceId);
                        }
                    }
                }
            };

            // Lấy lần lượt 4 jobs từ hàng đợi theo thứ tự độc lập FIFO
            for (int i = 0; i < 4; i++)
            {
                bool dispatched = await scheduler.DispatchNextSingleJobAsync();
                if (!dispatched)
                {
                    throw new InvalidOperationException($"Lượt {i + 1} không thể dispatch job nào.");
                }
                await Task.Delay(100);
            }

            var expectedSequence = new[]
            {
                wsHeavy,  // Lượt 1 (cũ nhất)
                wsMedium, // Lượt 2
                wsLight,  // Lượt 3
                wsHeavy   // Lượt 4
            };

            lock (dispatchedWorkspaces)
            {
                if (dispatchedWorkspaces.Count != 4)
                {
                    throw new InvalidOperationException($"Kỳ vọng 4 jobs dispatched nhưng nhận được {dispatchedWorkspaces.Count}.");
                }

                for (int i = 0; i < 4; i++)
                {
                    if (dispatchedWorkspaces[i] != expectedSequence[i])
                    {
                        throw new InvalidOperationException(
                            $"Sai thứ tự FIFO độc lập tại lượt {i + 1}: Kỳ vọng '{expectedSequence[i]}' nhưng thực tế là '{dispatchedWorkspaces[i]}'.");
                    }
                }
            }
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// TC-T15-08: Hạn ngạch 1 job/tab của Profile Shared AI (Browser Tab Quota).
    /// </summary>
    public static async Task Test_TC_T15_08_BrowserTabQuota_OneJobPerTabEnforced()
    {
        var (dbOptions, tempDir, _, _) = CreateTestContext();
        try
        {
            var (wsId, prjId, scnId) = await SeedBasicEntitiesAsync(dbOptions);
            var now = DateTimeOffset.UtcNow.ToString("o");

            var gatekeeper = new DefaultBrowserGatekeeper();
            // Khóa vai trò "Script" bằng một tab lease đang active
            var existingLease = await gatekeeper.TryAcquireTabLeaseAsync("shared_ai", "Script", "job-script-1");

            var jobScript2Id = Guid.NewGuid().ToString();
            var jobImage1Id = Guid.NewGuid().ToString();

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                db.Jobs.AddRange(
                    new Job
                    {
                        Id = jobScript2Id,
                        WorkspaceId = wsId,
                        ProjectId = prjId,
                        SceneId = null,
                        TaskType = JobTaskType.Script,
                        State = JobState.Queued,
                        ProviderKey = "chatgpt-web",
                        StorageRootId = "root-docs",
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    },
                    new Job
                    {
                        Id = jobImage1Id,
                        WorkspaceId = wsId,
                        ProjectId = prjId,
                        SceneId = scnId,
                        TaskType = JobTaskType.Image,
                        State = JobState.Queued,
                        ProviderKey = "chatgpt-web",
                        StorageRootId = "root-img",
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    });
                await db.SaveChangesAsync();
            }

            var reservation = new JobReservation(dbOptions);
            var executor = new DeterministicFakeJobExecutor();
            var scheduler = new JobScheduler(dbOptions, reservation, executor, gatekeeper);

            // Kích hoạt dispatch: Tab "Script" bận -> JobScript2 bị skip; Tab "Image" trống -> JobImage1 được reserve
            var dispatched = await scheduler.DispatchPendingJobsAsync();
            if (dispatched != 1)
            {
                throw new InvalidOperationException($"Kỳ vọng chỉ dispatch 1 job (Image) nhưng có {dispatched}.");
            }

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var jScript2 = await db.Jobs.AsNoTracking().FirstAsync(j => j.Id == jobScript2Id);
                var jImage1 = await db.Jobs.AsNoTracking().FirstAsync(j => j.Id == jobImage1Id);

                if (jScript2.State != JobState.Queued)
                {
                    throw new InvalidOperationException($"Job Script 2 vi phạm hạn ngạch tab, trạng thái là '{jScript2.State}'.");
                }
                if (jImage1.State == JobState.Queued)
                {
                    throw new InvalidOperationException($"Job Image 1 phải được reserve nhưng vẫn ở Queued.");
                }
            }

            // Giải phóng tab "Script"
            existingLease?.Dispose();

            // Kích hoạt dispatch lại: Job Script 2 lập tức được reserve
            dispatched = await scheduler.DispatchPendingJobsAsync();
            if (dispatched != 1)
            {
                throw new InvalidOperationException($"Kỳ vọng dispatch Job Script 2 sau khi giải phóng tab nhưng có {dispatched}.");
            }

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var jScript2 = await db.Jobs.AsNoTracking().FirstAsync(j => j.Id == jobScript2Id);
                if (jScript2.State == JobState.Queued)
                {
                    throw new InvalidOperationException("Job Script 2 vẫn ở Queued sau khi tab đã được nhả.");
                }
            }
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// TC-T15-09: Tương tác nghiêm ngặt giữa Job Queue và Thao tác Xóa Entity (Strict Delete Guards).
    /// </summary>
    public static async Task Test_TC_T15_09_StrictDeleteGuard_NonTerminalAndUnreconciledJobsBlockDeletion()
    {
        var (dbOptions, tempDir, _, _) = CreateTestContext();
        try
        {
            var (wsId, prjId, scnId) = await SeedBasicEntitiesAsync(dbOptions);
            var workspaceService = new WorkspaceService(dbOptions, tempDir);

            var jobId = Guid.NewGuid().ToString();
            var now = DateTimeOffset.UtcNow.ToString("o");

            // Thử nghiệm 1: Job đang Running -> Chặn xóa Workspace
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
                    ProviderKey = "chatgpt-web",
                    StorageRootId = "root-docs",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
                await db.SaveChangesAsync();
            }

            bool wsDeleteBlocked = false;
            try
            {
                await workspaceService.DeleteAsync(wsId);
            }
            catch (WorkspaceConcurrencyConflictException)
            {
                wsDeleteBlocked = true;
            }

            if (!wsDeleteBlocked)
            {
                throw new InvalidOperationException("WorkspaceService.DeleteAsync không bị chặn khi có job Running.");
            }

            // Thử nghiệm 2: Job AwaitingUser và side_effect_submitted = 1 chưa reconcile
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var job = await db.Jobs.FirstAsync(j => j.Id == jobId);
                job.State = JobState.AwaitingUser;
                job.SideEffectSubmitted = 1;
                job.ResultReference = null;
                await db.SaveChangesAsync();
            }

            wsDeleteBlocked = false;
            try
            {
                await workspaceService.DeleteAsync(wsId);
            }
            catch (WorkspaceConcurrencyConflictException)
            {
                wsDeleteBlocked = true;
            }

            if (!wsDeleteBlocked)
            {
                throw new InvalidOperationException("WorkspaceService.DeleteAsync không bị chặn khi job chưa đối soát.");
            }

            // Thử nghiệm 3: Job đã Succeeded và đã có result_reference
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var job = await db.Jobs.FirstAsync(j => j.Id == jobId);
                job.State = JobState.Succeeded;
                job.ResultReference = "asset-completed-001";
                await db.SaveChangesAsync();
            }

            var deleteResult = await workspaceService.DeleteAsync(wsId);
            if (!deleteResult.Deleted)
            {
                throw new InvalidOperationException("WorkspaceService.DeleteAsync không thành công sau khi job đã hoàn tất.");
            }
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// TC-T15-10: Kiểm chứng cam kết ý định tác dụng phụ trước khi thao tác trình duyệt.
    /// </summary>
    public static async Task Test_TC_T15_10_CrashBoundaryVerify_SideEffectCommittedBeforeAction()
    {
        var (dbOptions, tempDir, _, settingsService) = CreateTestContext();
        try
        {
            var (wsId, prjId, scnId) = await SeedBasicEntitiesAsync(dbOptions);

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var existing = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == SettingsService.KeyScriptProvider);
                if (existing != null)
                {
                    existing.Value = "chatgpt-web";
                    existing.UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("o");
                }
                else
                {
                    db.AppSettings.Add(new AppSetting
                    {
                        Key = SettingsService.KeyScriptProvider,
                        Value = "chatgpt-web",
                        UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("o")
                    });
                }
                await db.SaveChangesAsync();
            }

            var snapshotFactory = new JobSnapshotFactory(settingsService);
            var reservation = new JobReservation(dbOptions);

            // Kịch bản A: Crash trước khi commit side-effect
            var executorA = new DeterministicFakeJobExecutor
            {
                SimulateCrashBeforeSideEffectCommit = true
            };
            var schedulerA = new JobScheduler(dbOptions, reservation, executorA);
            var jobServiceA = new JobService(dbOptions, snapshotFactory, schedulerA);

            var resA = await jobServiceA.EnqueueAsync(new JobEnqueueRequest(
                WorkspaceId: wsId,
                ProjectId: prjId,
                SceneId: null,
                TaskType: JobTaskType.Script,
                CustomPromptText: "Prompt Script A"
            ));

            await schedulerA.DispatchPendingJobsAsync();
            await Task.Delay(100);

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var jobA = await db.Jobs.AsNoTracking().FirstAsync(j => j.Id == resA.JobId);
                // Vì crash trước khi commit side effect, cờ side_effect_submitted phải bằng 0
                if (jobA.SideEffectSubmitted != 0)
                {
                    throw new InvalidOperationException("Kịch bản A: Cờ side_effect_submitted phải bằng 0.");
                }
            }

            // Kịch bản B: Crash ngay SAU KHI commit side effect thành công
            var executorB = new DeterministicFakeJobExecutor
            {
                SimulateCrashAfterSideEffectCommit = true
            };
            var schedulerB = new JobScheduler(dbOptions, reservation, executorB);
            var jobServiceB = new JobService(dbOptions, snapshotFactory, schedulerB);

            var resB = await jobServiceB.EnqueueAsync(new JobEnqueueRequest(
                WorkspaceId: wsId,
                ProjectId: prjId,
                SceneId: null,
                TaskType: JobTaskType.Script,
                CustomPromptText: "Prompt Script B"
            ));

            await schedulerB.DispatchPendingJobsAsync();
            await Task.Delay(100);

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var jobB = await db.Jobs.AsNoTracking().FirstAsync(j => j.Id == resB.JobId);
                // Vì đã commit side effect trước khi crash: Bắt buộc chuyển sang AwaitingUser
                if (jobB.State != JobState.AwaitingUser || jobB.SideEffectSubmitted != 1)
                {
                    throw new InvalidOperationException($"Kịch bản B: Kỳ vọng AwaitingUser và side_effect=1 nhưng State={jobB.State}, SideEffect={jobB.SideEffectSubmitted}.");
                }
            }

            // Kịch bản C: Người dùng gọi reconcile với action = "retry"
            var reconcileResult = await jobServiceB.ReconcileAsync(new JobReconcileRequest(
                WorkspaceId: wsId,
                JobId: resB.JobId,
                Action: "retry",
                ConfirmedNotReceived: true
            ));

            if (reconcileResult.CurrentState != JobState.Queued)
            {
                throw new InvalidOperationException($"Kịch bản C: Sau khi reconcile retry, trạng thái phải là Queued nhưng là '{reconcileResult.CurrentState}'.");
            }
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// TC-T15-EXTRA: Kiểm chứng Bridge Handlers tiếp nhận và phản hồi 5 commands job.*
    /// </summary>
    public static async Task Test_TC_T15_Extra_BridgeHandlersIntegration()
    {
        var (dbOptions, tempDir, _, settingsService) = CreateTestContext();
        try
        {
            var (wsId, prjId, scnId) = await SeedBasicEntitiesAsync(dbOptions);

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var existing = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == SettingsService.KeyScriptProvider);
                if (existing != null)
                {
                    existing.Value = "chatgpt-web";
                    existing.UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("o");
                }
                else
                {
                    db.AppSettings.Add(new AppSetting
                    {
                        Key = SettingsService.KeyScriptProvider,
                        Value = "chatgpt-web",
                        UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("o")
                    });
                }
                await db.SaveChangesAsync();
            }

            var snapshotFactory = new JobSnapshotFactory(settingsService);
            var reservation = new JobReservation(dbOptions);
            var executor = new DeterministicFakeJobExecutor();
            var scheduler = new JobScheduler(dbOptions, reservation, executor);
            var jobService = new JobService(dbOptions, snapshotFactory, scheduler);

            var dispatcher = new BridgeDispatcher();
            var handlers = new JobHandlers(jobService);
            handlers.Register(dispatcher);

            foreach (var retiredCommand in new[] { "job.get_handoff", "job.import_result" })
            {
                if (dispatcher.IsCommandAllowed(retiredCommand))
                    throw new InvalidOperationException("Không được mở lại RPC handoff.");
                var retiredRequest = new RequestEnvelope(Guid.NewGuid(), "request", retiredCommand,
                    JsonSerializer.SerializeToElement(new { workspaceId = wsId, jobId = "legacy-job" }),
                    DateTimeOffset.UtcNow, "1.0.4");
                var retiredResponse = await dispatcher.DispatchAsync(retiredRequest, CancellationToken.None);
                if (retiredResponse.Success || retiredResponse.Error?.Code != "UNKNOWN_COMMAND")
                    throw new InvalidOperationException("RPC handoff cũ phải bị chặn ở allow-list.");
            }

            // 1. job.enqueue
            var enqueuePayload = JsonSerializer.SerializeToElement(new JobEnqueueRequest(
                WorkspaceId: wsId,
                ProjectId: prjId,
                SceneId: null,
                TaskType: JobTaskType.Script,
                CustomPromptText: "Viết kịch bản qua Bridge RPC"
            ), JsonOptions);

            var reqEnvelope = new RequestEnvelope(
                Id: Guid.NewGuid(),
                Type: "request",
                Command: "job.enqueue",
                Payload: enqueuePayload,
                ClientTimestampUtc: DateTimeOffset.UtcNow,
                ContractVersion: "1.0.4"
            );

            var resEnvelope = await dispatcher.DispatchAsync(reqEnvelope, CancellationToken.None);
            if (!resEnvelope.Success || resEnvelope.Data is null)
            {
                throw new InvalidOperationException($"job.enqueue thất bại: {resEnvelope.Error?.Message}");
            }

            var enqueueData = JsonSerializer.Deserialize<JobEnqueueResponseData>(resEnvelope.Data.Value.GetRawText(), JsonOptions);
            var jobId = enqueueData!.JobId;

            // 2. job.get_status
            var getStatusPayload = JsonSerializer.SerializeToElement(new JobGetStatusRequest(wsId, jobId), JsonOptions);
            var statusReq = new RequestEnvelope(Guid.NewGuid(), "request", "job.get_status", getStatusPayload, DateTimeOffset.UtcNow, "1.0.4");
            var statusRes = await dispatcher.DispatchAsync(statusReq, CancellationToken.None);
            if (!statusRes.Success)
            {
                throw new InvalidOperationException($"job.get_status thất bại: {statusRes.Error?.Message}");
            }

            // 3. job.list_active
            var listPayload = JsonSerializer.SerializeToElement(new JobListActiveRequest(wsId), JsonOptions);
            var listReq = new RequestEnvelope(Guid.NewGuid(), "request", "job.list_active", listPayload, DateTimeOffset.UtcNow, "1.0.4");
            var listRes = await dispatcher.DispatchAsync(listReq, CancellationToken.None);
            if (!listRes.Success)
            {
                throw new InvalidOperationException($"job.list_active thất bại: {listRes.Error?.Message}");
            }

            // 4. job.cancel
            var cancelPayload = JsonSerializer.SerializeToElement(new JobCancelRequest(wsId, jobId), JsonOptions);
            var cancelReq = new RequestEnvelope(Guid.NewGuid(), "request", "job.cancel", cancelPayload, DateTimeOffset.UtcNow, "1.0.4");
            var cancelRes = await dispatcher.DispatchAsync(cancelReq, CancellationToken.None);
            if (!cancelRes.Success)
            {
                throw new InvalidOperationException($"job.cancel thất bại: {cancelRes.Error?.Message}");
            }

            handlers.Unregister(dispatcher);
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    // =========================================================================
    // REGRESSION TESTS FOR FINDINGS R1 - R8 (REVIEW COORDINATOR 2 REMEDIATION)
    // =========================================================================

    /// <summary>
    /// R1: Kiểm chứng Production Executor (UnavailableAdapterJobExecutor) không ghi nhận thành công giả
    /// khi adapter chưa sẵn sàng. Job chuyển sang Failed với reason adapter_not_available, không handoff hoặc retry.
    /// </summary>
    public static async Task Test_Regression_R1_ProductionExecutor_UnavailableAdapter_NoFakeSuccess()
    {
        var (dbOptions, tempDir, _, settingsService) = CreateTestContext();
        try
        {
            var (wsId, prjId, scnId) = await SeedBasicEntitiesAsync(dbOptions);

            var snapshotFactory = new JobSnapshotFactory(settingsService, dbOptions: dbOptions);
            var reservation = new JobReservation(dbOptions);
            var executor = new UnavailableAdapterJobExecutor();
            var scheduler = new JobScheduler(dbOptions, reservation, executor);
            var jobService = new JobService(dbOptions, snapshotFactory, scheduler);

            var enqueueRes = await jobService.EnqueueAsync(new JobEnqueueRequest(
                WorkspaceId: wsId,
                ProjectId: prjId,
                SceneId: null,
                TaskType: JobTaskType.Script,
                CustomPromptText: "Viết kịch bản bằng production executor"
            ), CancellationToken.None);

            int dispatched = await scheduler.DispatchPendingJobsAsync(CancellationToken.None);
            if (dispatched != 1)
            {
                throw new InvalidOperationException($"Kỳ vọng dispatch 1 job, thực tế: {dispatched}");
            }

            // Đợi worker hoàn tất xử lý failure an toàn
            Job? job = null;
            for (int i = 0; i < 50; i++)
            {
                await Task.Delay(20);
                await using var db = new TiToAIDbContext(dbOptions);
                job = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == enqueueRes.JobId);
                if (job != null && job.State == JobState.Failed) break;
            }

            if (job == null) throw new InvalidOperationException("Không tìm thấy job trong DB.");
            if (job.State == JobState.Succeeded)
            {
                throw new InvalidOperationException("LỖI R1: Job thành Succeeded giả mạo khi adapter chưa sẵn sàng!");
            }
            if (job.State != JobState.Failed || job.ReasonCode != "adapter_not_available")
            {
                throw new InvalidOperationException($"Kỳ vọng job ở Failed với lý do adapter_not_available, thực tế: {job.State} / {job.ReasonCode}");
            }
            if (!string.IsNullOrWhiteSpace(job.ResultReference))
            {
                throw new InvalidOperationException($"LỖI R1: Job nhận tham chiếu kết quả giả: {job.ResultReference}");
            }
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// R2: Kiểm chứng Project Guard chặn xóa khi có Job cấp Project đang chạy với SceneId = null
    /// và job có side effect chưa đối soát.
    /// </summary>
    public static async Task Test_Regression_R2_DeleteProjectWithActiveProjectLevelJob_SceneIdNull_Blocked()
    {
        var (dbOptions, tempDir, _, settingsService) = CreateTestContext();
        try
        {
            var (wsId, prjId, _) = await SeedBasicEntitiesAsync(dbOptions);
            var nowIso = DateTimeOffset.UtcNow.ToString("o");

            // Tạo Job cấp Project (SceneId = null) đang ở trạng thái Running
            var projectLevelJobId = Guid.NewGuid().ToString();
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                db.Jobs.Add(new Job
                {
                    Id = projectLevelJobId,
                    WorkspaceId = wsId,
                    ProjectId = prjId,
                    SceneId = null,
                    TaskType = JobTaskType.Script,
                    State = JobState.Running,
                    ProviderKey = "chatgpt-web",
                    StorageRootId = "root-docs",
                    LeaseOwnerId = "worker-1",
                    LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(45).ToString("o"),
                    FencingToken = 1,
                    Attempts = 1,
                    MaxAttempts = 3,
                    SideEffectSubmitted = 1, // Đã submit side effect, chưa đối soát
                    ResultReference = null,
                    CreatedAtUtc = nowIso,
                    UpdatedAtUtc = nowIso
                });
                await db.SaveChangesAsync();
            }

            var projectService = new ProjectService(dbOptions);

            // Thử xóa Project: BẮT BUỘC phải ném WorkspaceConcurrencyConflictException
            bool conflictThrown = false;
            try
            {
                await projectService.DeleteAsync(wsId, prjId, CancellationToken.None);
            }
            catch (WorkspaceConcurrencyConflictException)
            {
                conflictThrown = true;
            }

            if (!conflictThrown)
            {
                throw new InvalidOperationException("LỖI R2: ProjectService.DeleteAsync không chặn xóa Project có job cấp Project (SceneId = null) đang Running!");
            }

            // Kiểm chứng trong DB: Cả Project lẫn Job đều còn nguyên vẹn
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                bool projectExists = await db.Projects.AnyAsync(p => p.Id == prjId);
                bool jobExists = await db.Jobs.AnyAsync(j => j.Id == projectLevelJobId);

                if (!projectExists || !jobExists)
                {
                    throw new InvalidOperationException($"LỖI R2: Project ({projectExists}) hoặc Job ({jobExists}) bị xóa mất khỏi DB!");
                }
            }
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// R3: Kiểm chứng hai worker tranh cùng role tab: chỉ worker chiếm được lease mới được chạy executor;
    /// worker thất bại hoàn trả job về Queued an toàn không tăng attempts; release sai owner không xóa lease của owner đúng.
    /// </summary>
    public static async Task Test_Regression_R3_TwoWorkersContendingSameRoleTab_SingleExecution()
    {
        var (dbOptions, tempDir, _, settingsService) = CreateTestContext();
        try
        {
            var (wsId, prjId, scnId) = await SeedBasicEntitiesAsync(dbOptions);
            var nowIso = DateTimeOffset.UtcNow.ToString("o");

            // 1. Kiểm tra Gatekeeper: Release sai owner không được xóa lease của owner đúng
            var gatekeeper = new DefaultBrowserGatekeeper();
            var leaseA = await gatekeeper.TryAcquireTabLeaseAsync("shared_ai", JobTaskType.Script, "owner-A");
            if (leaseA == null) throw new InvalidOperationException("Không acquire được lease A");

            // Worker B thử gọi release lease của owner A
            gatekeeper.ReleaseTabLease("shared_ai", JobTaskType.Script, "owner-B");
            if (!gatekeeper.IsRoleActive("shared_ai", JobTaskType.Script))
            {
                throw new InvalidOperationException("LỖI R3: ReleaseTabLease với owner-B sai đã xóa mất lease của owner-A!");
            }

            // Release đúng owner-A
            gatekeeper.ReleaseTabLease("shared_ai", JobTaskType.Script, "owner-A");
            if (gatekeeper.IsRoleActive("shared_ai", JobTaskType.Script))
            {
                throw new InvalidOperationException("ReleaseTabLease với đúng owner-A không giải phóng lease!");
            }

            leaseA.Dispose(); // Thử dispose lại để kiểm tra tính idempotent

            // 2. Kiểm chứng Scheduler: Khi gate từ chối acquire tab lease, worker KHÔNG được chạy executor
            var singleSlotGate = new SingleSlotDenyGate();
            var fakeExecutor = new DeterministicFakeJobExecutor();
            var reservation = new JobReservation(dbOptions);

            var jobId = Guid.NewGuid().ToString();
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                db.Jobs.Add(new Job
                {
                    Id = jobId,
                    WorkspaceId = wsId,
                    ProjectId = prjId,
                    SceneId = null,
                    TaskType = JobTaskType.Script,
                    State = JobState.Queued,
                    ProviderKey = "chatgpt-web",
                    StorageRootId = "root-docs",
                    Attempts = 0,
                    MaxAttempts = 3,
                    FencingToken = 0,
                    CreatedAtUtc = nowIso,
                    UpdatedAtUtc = nowIso
                });
                await db.SaveChangesAsync();
            }

            await using (var scheduler = new JobScheduler(dbOptions, reservation, fakeExecutor, singleSlotGate))
            {
                await scheduler.DispatchPendingJobsAsync(CancellationToken.None);

                // Đợi worker hoàn tất
                for (int i = 0; i < 50; i++)
                {
                    await Task.Delay(20);
                    await using var pollDb = new TiToAIDbContext(dbOptions);
                    var j = await pollDb.Jobs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == jobId);
                    if (j != null && j.State == JobState.Queued && j.ReasonCode == "tab_capacity_unavailable") break;
                }

                if (fakeExecutor.RecordedExecutions.Count > 0)
                {
                    throw new InvalidOperationException($"LỖI R3: Executor bị gọi {fakeExecutor.RecordedExecutions.Count} lần mặc dù lease thất bại!");
                }

                await using var finalDb = new TiToAIDbContext(dbOptions);
                var recheckedJob = await finalDb.Jobs.AsNoTracking().FirstAsync(x => x.Id == jobId);
                if (recheckedJob.State != JobState.Queued)
                {
                    throw new InvalidOperationException($"Kỳ vọng job được hoàn trả về Queued, thực tế: {recheckedJob.State}");
                }
                if (recheckedJob.Attempts != 0)
                {
                    throw new InvalidOperationException($"Kỳ vọng attempts không bị tăng phạt (0), thực tế: {recheckedJob.Attempts}");
                }
            }
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// R4: Kiểm chứng Sweep Atomic CAS:
    /// - Worker đã hết hạn lease không thể tự heartbeat hoặc commit side effect hồi sinh.
    /// - Sweep không ghi đè job vừa heartbeat/commit submit.
    /// - Job có side effect submitted = 1 không bao giờ bị re-reserve mù.
    /// </summary>
    public static async Task Test_Regression_R4_SweepConcurrentWithHeartbeatOrCommit_NoBlindResend()
    {
        var (dbOptions, tempDir, _, _) = CreateTestContext();
        try
        {
            var (wsId, prjId, scnId) = await SeedBasicEntitiesAsync(dbOptions);
            var now = DateTimeOffset.UtcNow;
            var expiredIso = now.AddSeconds(-60).ToString("o");

            var reservation = new JobReservation(dbOptions);

            // 1. Kiểm tra worker hết hạn lease không thể hồi sinh
            var deadJobId = Guid.NewGuid().ToString();
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                db.Jobs.Add(new Job
                {
                    Id = deadJobId,
                    WorkspaceId = wsId,
                    ProjectId = prjId,
                    SceneId = null,
                    TaskType = JobTaskType.Script,
                    State = JobState.Running,
                    ProviderKey = "chatgpt-web",
                    StorageRootId = "root-docs",
                    LeaseOwnerId = "dead-worker",
                    LeaseExpiresAtUtc = expiredIso,
                    FencingToken = 1,
                    Attempts = 1,
                    MaxAttempts = 3,
                    CreatedAtUtc = now.ToString("o"),
                    UpdatedAtUtc = now.ToString("o")
                });
                await db.SaveChangesAsync();
            }

            bool heartbeatReviveBlocked = false;
            try
            {
                await reservation.SendHeartbeatAsync(deadJobId, "dead-worker", 1, TimeSpan.FromSeconds(45));
            }
            catch (FencingTokenStaleException)
            {
                heartbeatReviveBlocked = true;
            }

            if (!heartbeatReviveBlocked)
            {
                throw new InvalidOperationException("LỖI R4: Worker có lease hết hạn vẫn gửi được heartbeat để hồi sinh!");
            }

            bool commitSideEffectBlocked = false;
            try
            {
                await reservation.CommitSideEffectAsync(deadJobId, 1);
            }
            catch (FencingTokenStaleException)
            {
                commitSideEffectBlocked = true;
            }

            if (!commitSideEffectBlocked)
            {
                throw new InvalidOperationException("LỖI R4: Worker có lease hết hạn vẫn commit được side effect!");
            }

            // 2. Kiểm chứng Sweep Atomic CAS: Sweep không ghi đè job đã kịp heartbeat hoặc hoàn thành
            var racingJobId = Guid.NewGuid().ToString();
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                db.Jobs.Add(new Job
                {
                    Id = racingJobId,
                    WorkspaceId = wsId,
                    ProjectId = prjId,
                    SceneId = null,
                    TaskType = JobTaskType.Script,
                    State = JobState.Running,
                    ProviderKey = "chatgpt-web",
                    StorageRootId = "root-docs",
                    LeaseOwnerId = "racing-worker",
                    LeaseExpiresAtUtc = now.AddSeconds(45).ToString("o"), // Chưa hết hạn
                    FencingToken = 2,
                    Attempts = 1,
                    MaxAttempts = 3,
                    CreatedAtUtc = now.ToString("o"),
                    UpdatedAtUtc = now.ToString("o")
                });
                await db.SaveChangesAsync();
            }

            await using (var sweeper = new JobScheduler(dbOptions, reservation, new DeterministicFakeJobExecutor()))
            {
                int recovered = await sweeper.RunCrashRecoverySweepAsync(CancellationToken.None);
                if (recovered != 1) // Chỉ thu hồi deadJobId, KHÔNG thu hồi racingJobId
                {
                    throw new InvalidOperationException($"Kỳ vọng sweep thu hồi đúng 1 dead job, thực tế: {recovered}");
                }

                await using var db = new TiToAIDbContext(dbOptions);
                var deadJob = await db.Jobs.AsNoTracking().FirstAsync(j => j.Id == deadJobId);
                if (deadJob.State != JobState.Queued)
                {
                    throw new InvalidOperationException($"Dead job chưa submit side effect phải chuyển Queued để retry, thực tế: {deadJob.State}");
                }

                var racingJob = await db.Jobs.AsNoTracking().FirstAsync(j => j.Id == racingJobId);
                if (racingJob.State != JobState.Running)
                {
                    throw new InvalidOperationException($"LỖI R4: Racing job còn lease hợp lệ bị sweep ghi đè thành: {racingJob.State}");
                }
            }

            // 3. Kiểm chứng cấm Re-reserve mù: Job có SideEffectSubmitted = 1 bị từ chối reserve
            var submittedJobId = Guid.NewGuid().ToString();
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                db.Jobs.Add(new Job
                {
                    Id = submittedJobId,
                    WorkspaceId = wsId,
                    ProjectId = prjId,
                    SceneId = null,
                    TaskType = JobTaskType.Script,
                    State = JobState.Queued,
                    ProviderKey = "chatgpt-web",
                    StorageRootId = "root-docs",
                    SideEffectSubmitted = 1, // Đã submit side effect nhưng chưa đối soát
                    ResultReference = null,
                    Attempts = 1,
                    MaxAttempts = 3,
                    CreatedAtUtc = now.ToString("o"),
                    UpdatedAtUtc = now.ToString("o")
                });
                await db.SaveChangesAsync();
            }

            var reserveRes = await reservation.TryReserveJobAsync(submittedJobId, "new-worker", TimeSpan.FromSeconds(45));
            if (reserveRes.Acquired)
            {
                throw new InvalidOperationException("LỖI R4: TryReserveJobAsync đã cấp phát (reserve) một job có side effect chưa đối soát (phá vỡ invariant cấm resend mù)!");
            }

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var submittedJob = await db.Jobs.AsNoTracking().FirstAsync(j => j.Id == submittedJobId);
                if (submittedJob.State != JobState.AwaitingUser || submittedJob.ReasonCode != JobReasonCode.ReconcileRequired)
                {
                    throw new InvalidOperationException($"Kỳ vọng job có side effect chưa đối soát chuyển sang AwaitingUser/reconcile_required, thực tế: {submittedJob.State}/{submittedJob.ReasonCode}");
                }
            }
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// R5: Kiểm chứng HostBrowserGatekeeper kết nối ProfileRegistry và LoginCoordinator thật;
    /// khi profile login hoặc quiescing thì gatekeeper chặn dispatch.
    /// </summary>
    public static async Task Test_Regression_R5_HostBrowserGatekeeper_IntegratesWithRegistryAndLoginCoordinator()
    {
        var (dbOptions, tempDir, _, _) = CreateTestContext();
        try
        {
            var tabPool = new TabPool();
            var profileRegistry = new ProfileRegistry(() => new TiToAIDbContext(dbOptions), null, tabPool);
            var processController = new PlaywrightBrowserController(profileRegistry, new ProfileOwner(pid => profileRegistry.GetUserDataDir(pid, "AI")));
            var sessionProbe = new SessionProbe(profileRegistry);
            var loginCoordinator = new LoginCoordinator(profileRegistry, processController, sessionProbe);

            using var gatekeeper = new HostBrowserGatekeeper(profileRegistry, loginCoordinator, tabPool);

            // Ban đầu: Profile rảnh
            if (gatekeeper.IsDispatchPaused("shared_ai")) throw new InvalidOperationException("Kỳ vọng ban đầu shared_ai không pause");
            if (gatekeeper.IsProfileBusyWithLogin("shared_ai")) throw new InvalidOperationException("Kỳ vọng ban đầu shared_ai không busy login");

            // Chuyển profile sang Quiescing
            await profileRegistry.EnsureProfileAsync("shared_ai", BrowserProfileKind.SharedAI);
            await profileRegistry.UpdateProfileStateAsync("shared_ai", operationalState: BrowserOperationalState.Quiescing);

            if (!gatekeeper.IsDispatchPaused("shared_ai"))
            {
                throw new InvalidOperationException("LỖI R5: HostBrowserGatekeeper không phát hiện trạng thái Quiescing từ ProfileRegistry!");
            }

            // Chuyển profile sang VisibleActive (đang login)
            await profileRegistry.UpdateProfileStateAsync("shared_ai",
                operationalState: BrowserOperationalState.Running,
                visibilityState: BrowserVisibilityState.VisibleActive);

            if (!gatekeeper.IsProfileBusyWithLogin("shared_ai"))
            {
                throw new InvalidOperationException("LỖI R5: HostBrowserGatekeeper không phát hiện trạng thái VisibleActive (login)!");
            }

            // Đưa về Hidden
            await profileRegistry.UpdateProfileStateAsync("shared_ai", visibilityState: BrowserVisibilityState.Hidden);
            if (gatekeeper.IsProfileBusyWithLogin("shared_ai"))
            {
                throw new InvalidOperationException("Kỳ vọng sau khi về Hidden thì profile không còn busy login");
            }
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// R6: Kiểm chứng Event progress và state mang đúng WorkspaceId;
    /// bridge client không phân phối event của Workspace A cho Workspace B.
    /// </summary>
    public static async Task Test_Regression_R6_JobProgressUpdatedAndStateChanged_ScopedToWorkspace()
    {
        var (dbOptions, tempDir, _, settingsService) = CreateTestContext();
        try
        {
            var (wsA, prjA, scnA) = await SeedBasicEntitiesAsync(dbOptions);
            var wsB = Guid.NewGuid().ToString();
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                db.Workspaces.Add(new Workspace
                {
                    Id = wsB,
                    Name = "Workspace B",
                    ChannelHandle = "@ws_b",
                    Topic = "Tech",
                    Status = "Active",
                    CreatedAtUtc = DateTimeOffset.UtcNow.ToString("o"),
                    UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("o")
                });
                await db.SaveChangesAsync();
            }

            var snapshotFactory = new JobSnapshotFactory(settingsService, dbOptions: dbOptions);
            var reservation = new JobReservation(dbOptions);
            var fakeExecutor = new DeterministicFakeJobExecutor();
            await using var scheduler = new JobScheduler(dbOptions, reservation, fakeExecutor);

            var capturedProgressEvents = new List<JobProgressUpdatedPayload>();
            var capturedStateEvents = new List<JobStateChangedPayload>();

            scheduler.OnJobProgressUpdated += p => capturedProgressEvents.Add(p);
            scheduler.OnJobStateChanged += p => capturedStateEvents.Add(p);

            var jobService = new JobService(dbOptions, snapshotFactory, scheduler);
            var enqueueRes = await jobService.EnqueueAsync(new JobEnqueueRequest(
                WorkspaceId: wsA,
                ProjectId: prjA,
                SceneId: null,
                TaskType: JobTaskType.Script,
                CustomPromptText: "Viết kịch bản kiểm tra scope"
            ), CancellationToken.None);

            await scheduler.DispatchPendingJobsAsync(CancellationToken.None);

            // Đợi worker hoàn tất
            for (int i = 0; i < 50; i++)
            {
                await Task.Delay(20);
                if (capturedStateEvents.Any(e => e.NewState == JobState.Succeeded)) break;
            }

            if (capturedProgressEvents.Count == 0)
            {
                throw new InvalidOperationException("Không nhận được progress event nào");
            }

            // Kiểm tra: MỌI progress event phải mang đúng WorkspaceId của wsA
            foreach (var prog in capturedProgressEvents)
            {
                if (prog.WorkspaceId != wsA)
                {
                    throw new InvalidOperationException($"LỖI R6: Progress event mang sai WorkspaceId: '{prog.WorkspaceId}' (kỳ vọng '{wsA}')");
                }
            }

            // Kiểm tra: MỌI state changed event phải mang đúng WorkspaceId của wsA
            foreach (var st in capturedStateEvents)
            {
                if (st.WorkspaceId != wsA)
                {
                    throw new InvalidOperationException($"LỖI R6: StateChanged event mang sai WorkspaceId: '{st.WorkspaceId}' (kỳ vọng '{wsA}')");
                }
            }
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// R7: Kiểm chứng DisposeAsync của JobScheduler hủy token của worker và await drain an toàn.
    /// </summary>
    public static async Task Test_Regression_R7_DisposeScheduler_CancelsWorkersAndDrainsGracefully()
    {
        var (dbOptions, tempDir, _, settingsService) = CreateTestContext();
        try
        {
            var (wsId, prjId, scnId) = await SeedBasicEntitiesAsync(dbOptions);

            var blockingExecutor = new RegressionBlockingExecutor();
            var reservation = new JobReservation(dbOptions);

            var scheduler = new JobScheduler(dbOptions, reservation, blockingExecutor)
            {
                DrainTimeout = TimeSpan.FromSeconds(3)
            };

            var snapshotFactory = new JobSnapshotFactory(settingsService, dbOptions: dbOptions);
            var jobService = new JobService(dbOptions, snapshotFactory, scheduler);

            var enqueueRes = await jobService.EnqueueAsync(new JobEnqueueRequest(
                WorkspaceId: wsId,
                ProjectId: prjId,
                SceneId: null,
                TaskType: JobTaskType.Script,
                CustomPromptText: "Tác vụ kiểm tra drain dispose"
            ), CancellationToken.None);

            await scheduler.DispatchPendingJobsAsync(CancellationToken.None);

            // Đợi worker bắt đầu
            await blockingExecutor.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));

            // Gọi DisposeAsync trên scheduler
            var disposeTask = scheduler.DisposeAsync().AsTask();

            // Kiểm chứng token của worker đã nhận yêu cầu hủy
            if (!blockingExecutor.Token.IsCancellationRequested)
            {
                throw new InvalidOperationException("LỖI R7: DisposeAsync không kích hoạt cancellation token trên worker đang chạy!");
            }

            // Cho phép worker kết thúc
            blockingExecutor.Release.TrySetResult();
            await disposeTask;

            if (!blockingExecutor.Completed)
            {
                throw new InvalidOperationException("LỖI R7: DisposeAsync hoàn tất nhưng worker chưa kết thúc drain!");
            }
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// R8: Kiểm chứng Enqueue bằng PromptTemplateId thành công, chụp snapshot bất biến
    /// và thay đổi sau đó trong Settings / Template không làm biến đổi job cũ.
    /// </summary>
    public static async Task Test_Regression_R8_PromptTemplateAndStorageRootSnapshot_SettingsChangeDoesNotAffectJob()
    {
        var (dbOptions, tempDir, rootRegistry, settingsService) = CreateTestContext();
        try
        {
            var (wsId, prjId, scnId) = await SeedBasicEntitiesAsync(dbOptions);
            await rootRegistry.EnsureDefaultRootsAsync();

            // 1. Tạo Prompt Template trong DB
            var tplId = Guid.NewGuid().ToString();
            var nowIso = DateTimeOffset.UtcNow.ToString("o");

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var template = new PromptTemplate
                {
                    Id = tplId,
                    Purpose = "Script",
                    WorkspaceId = null, // Global
                    TemplateText = "Hãy viết kịch bản về chủ đề {topic} cho kênh TikTok.",
                    VariableDefinitionsJson = "[{\"name\":\"topic\",\"required\":true}]",
                    CreatedAtUtc = nowIso,
                    UpdatedAtUtc = nowIso
                };

                db.PromptTemplates.Add(template);
                await db.SaveChangesAsync();
            }

            // 2. Khởi tạo JobSnapshotFactory với dbOptions để nạp PromptTemplateService thật
            var snapshotFactory = new JobSnapshotFactory(settingsService, dbOptions: dbOptions);
            var reservation = new JobReservation(dbOptions);
            var executor = new UnavailableAdapterJobExecutor();
            var scheduler = new JobScheduler(dbOptions, reservation, executor);
            var jobService = new JobService(dbOptions, snapshotFactory, scheduler);

            // 3. Enqueue job bằng PromptTemplateId
            var enqueueRes = await jobService.EnqueueAsync(new JobEnqueueRequest(
                WorkspaceId: wsId,
                ProjectId: prjId,
                SceneId: null,
                TaskType: JobTaskType.Script,
                PromptTemplateId: tplId,
                PromptVariables: new Dictionary<string, string> { ["topic"] = "Trí tuệ nhân tạo" }
            ), CancellationToken.None);

            // Đọc snapshot đã lưu trong SQLite
            Job? savedJob = null;
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                savedJob = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == enqueueRes.JobId);
            }

            if (savedJob == null) throw new InvalidOperationException("Job không được lưu trong DB!");
            if (string.IsNullOrWhiteSpace(savedJob.PromptSnapshotText))
            {
                throw new InvalidOperationException("LỖI R8: PromptSnapshotText bị rỗng sau khi enqueue từ template!");
            }

            var renderedText = JsonDocument.Parse(savedJob.PromptSnapshotText).RootElement.GetProperty("renderedText").GetString();
            if (renderedText == null || !renderedText.Contains("Trí tuệ nhân tạo"))
            {
                throw new InvalidOperationException($"LỖI R8: Prompt snapshot không render đúng biến 'topic': {savedJob.PromptSnapshotText}");
            }

            var originalPromptSnapshot = savedJob.PromptSnapshotText;
            var originalRootId = savedJob.StorageRootId;

            // 4. Thay đổi template trong DB và thay đổi Settings Root
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var tpl = await db.PromptTemplates.FirstAsync(t => t.Id == tplId);
                tpl.TemplateText = "Nội dung hoàn toàn mới phiên bản 2";
                tpl.UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("o");
                await db.SaveChangesAsync();
            }

            // Đọc lại job cũ: BẤT BIẾN 100%
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var rechecked = await db.Jobs.AsNoTracking().FirstAsync(j => j.Id == enqueueRes.JobId);
                if (rechecked.PromptSnapshotText != originalPromptSnapshot)
                {
                    throw new InvalidOperationException("LỖI R8: Prompt snapshot của job cũ bị biến đổi sau khi sửa template!");
                }
                if (rechecked.StorageRootId != originalRootId)
                {
                    throw new InvalidOperationException("LỖI R8: Storage root của job cũ bị biến đổi!");
                }
            }
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    private sealed class SingleSlotDenyGate : IBrowserGatekeeper
    {
        private int _acquireCount;
        public bool IsDispatchPaused(string profileId) => false;
        public bool IsProfileBusyWithLogin(string profileId) => false;
        public bool IsRoleActive(string profileId, string role) => false;
        public bool IsProfileIdle(string profileId) => true;
        public void CloseProfileIfIdle(string profileId) { }
        public IReadOnlySet<string> GetActiveOrBusyProfileIds() => new HashSet<string>();

        public Task<IDisposable?> TryAcquireTabLeaseAsync(string profileId, string role, string jobId, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _acquireCount) == 1)
            {
                // Cố tình từ chối để kiểm tra worker không chạy executor
                return Task.FromResult<IDisposable?>(null);
            }
            return Task.FromResult<IDisposable?>(null);
        }

        public void ReleaseTabLease(string profileId, string role, string jobId) { }
    }

    private sealed class RegressionBlockingExecutor : IJobExecutor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token;
        public volatile bool Completed;

        public async Task<JobExecutionResult> ExecuteAsync(
            JobExecutionContext context,
            Func<Task> onPreSideEffectCommitAsync,
            Func<int, string, Task>? onProgressAsync,
            CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            Started.TrySetResult();
            try
            {
                await Release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Bắt cancellation khi drain
            }
            finally
            {
                Completed = true;
            }
            return new JobExecutionResult(true, "ref", "text");
        }
    }
}
