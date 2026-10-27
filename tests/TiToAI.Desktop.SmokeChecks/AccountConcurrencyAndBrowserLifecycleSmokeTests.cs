using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using TiToAI.Application.Accounts;
using TiToAI.Application.Jobs;
using TiToAI.Application.Settings;
using TiToAI.Browser;
using TiToAI.Browser.Abstractions;
using TiToAI.Browser.Models;
using TiToAI.Contracts.Accounts;
using TiToAI.Contracts.Bridge;
using TiToAI.Contracts.Jobs;
using TiToAI.Desktop.Bridge.Handlers;
using TiToAI.Desktop.Hosting;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Persistence.Entities;
using TiToAI.Infrastructure.Persistence.Interceptors;
using TiToAI.Infrastructure.Storage;

namespace TiToAI.Desktop.SmokeChecks;

/// <summary>
/// Bộ kiểm thử khói định lượng xác minh:
/// 1. Giới hạn đồng thời N của tài khoản AI (MaxTabs làm nguồn cấu hình duy nhất, bỏ MaxJobs).
/// 2. Tạo/sửa tài khoản, lưu và reload giữ đúng N.
/// 3. N=2 với 5 job đủ điều kiện: tối đa 2 job chạy trên 2 tab; còn lại chờ; một job xong thì job tiếp theo được chạy.
/// 4. Tài khoản cũ có Max Jobs khác Max Tabs dùng Max Tabs làm giới hạn mới, không bị Max Jobs cũ chặn.
/// 5. Giảm N khi đang có job chạy: không hủy job đang chạy, dừng nhận thêm đến khi còn capacity.
/// 6. Vòng đời browser:
///    - Không tự mở browser khi mở app, load tài khoản hoặc chỉ vì tài khoản bật nhận việc.
///    - Chỉ mở browser automation khi đã reserve job đủ điều kiện, chạy ẩn.
///    - Mỗi job 1 tab riêng, cùng account dùng chung browser/profile.
///    - Hết job thì browser đóng và giải phóng tài nguyên.
///    - Lưu profile/cookie để lần chạy sau sử dụng lại phiên đăng nhập.
///    - Không đóng browser khi còn job chạy hoặc job chưa rõ kết quả cần đối soát.
///    - An toàn khi job mới đến trong lúc browser đang đóng, không tạo 2 browser cùng sở hữu 1 profile.
/// </summary>
public static class AccountConcurrencyAndBrowserLifecycleSmokeTests
{
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"[ASSERTION FAILED] {message}");
        }
    }

    private static (DbContextOptions<TiToAIDbContext> Options, string TempDir, StorageRootRegistry Registry, SettingsService Settings) CreateTestContext()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"titoai_concurrency_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var dbPath = Path.Combine(tempDir, "concurrency.db");
        var connectionString = DatabasePathHelper.BuildConnectionString(dbPath);

        var optionsBuilder = new DbContextOptionsBuilder<TiToAIDbContext>();
        optionsBuilder.UseSqlite(connectionString)
                      .AddInterceptors(new SqlitePragmaInterceptor())
                      .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        var dbOptions = optionsBuilder.Options;

        using (var db = new TiToAIDbContext(dbOptions))
        {
            db.Database.Migrate();
        }

        var registry = new StorageRootRegistry(dbOptions, tempDir);
        registry.EnsureDefaultRootsAsync().GetAwaiter().GetResult();
        var settingsService = new SettingsService(dbOptions, registry);

        return (dbOptions, tempDir, registry, settingsService);
    }

    private static void CleanupTestContext(string tempDir)
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
        catch { }
    }

    public static async Task<int> RunAllAsync()
    {
        Console.WriteLine("\n=================================================================");
        Console.WriteLine("    TiToAI — Account Concurrency & Browser Lifecycle Smoke Tests ");
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
            "1. Tạo/sửa tài khoản với MaxTabs làm N duy nhất, lưu và reload giữ đúng N",
            Test_AccountConcurrency_CreateAndUpdate_PersistsNAsync);

        await RunCaseAsync(
            "2. N=2 với 5 job đủ điều kiện: tối đa 2 job chạy trên 2 tab; job xong thì job tiếp được chạy",
            Test_AccountConcurrency_N2_With5Jobs_StreamingNextAsync);

        await RunCaseAsync(
            "3. Tài khoản cũ có MaxJobs=1 khác MaxTabs=3: dùng MaxTabs=3 làm giới hạn mới, không bị chặn ngầm",
            Test_AccountConcurrency_LegacyAccount_UsesMaxTabsAsCapAsync);

        await RunCaseAsync(
            "4. Giảm N khi đang có job chạy: không hủy job đang chạy, dừng nhận thêm đến khi còn capacity",
            Test_AccountConcurrency_ReduceNDuringRunningJobsAsync);

        await RunCaseAsync(
            "5. Vòng đời browser: Không mở khi mở app; có job thì mở ẩn; hết job thì đóng; chạy tiếp giữ phiên",
            Test_BrowserLifecycle_FullLifecycleWithSavedSessionAsync);

        await RunCaseAsync(
            "6. Vòng đời browser: An toàn khi job mới đến trong lúc browser đang đóng, không tạo 2 browser",
            Test_BrowserLifecycle_JobArrivalDuringClosing_SafeAsync);

        await RunCaseAsync(
            "7. Vòng đời browser: Không đóng browser khi còn job chạy hoặc job chưa rõ kết quả đối soát",
            Test_BrowserLifecycle_DoesNotCloseWhenJobActiveOrUncertainAsync);

        Console.WriteLine("\n=================================================================");
        if (failed == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"   TẤT CẢ {passed}/{passed} BÀI KIỂM THỬ GIỚI HẠN ĐỒNG THỜI & BROWSER ĐÃ PASS!");
            Console.ResetColor();
            Console.WriteLine("=================================================================");
            return 0;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"   THẤT BẠI: {failed} FAIL, {passed} PASS");
            Console.ResetColor();
            Console.WriteLine("=================================================================");
            return 1;
        }
    }

    /// <summary>
    /// Kịch bản 1: Tạo/sửa tài khoản, lưu và reload vẫn giữ đúng N.
    /// Kiểm tra nguồn cấu hình duy nhất là MaxTabs, MaxConcurrentJobs được đồng bộ với N.
    /// </summary>
    public static async Task Test_AccountConcurrency_CreateAndUpdate_PersistsNAsync()
    {
        var (dbOptions, tempDir, _, _) = CreateTestContext();
        try
        {
            var identityResolver = new FakeProfileIdentityResolver("Thomas (123456)");
            var accountService = new ProviderAccountService(dbOptions, identityResolver);

            // 1. Tạo tài khoản chỉ truyền MaxTabs = 2 (ô MaxJobs bị loại khỏi form)
            var createReq = new CreateProviderAccountRequest(
                ProviderKey: "chatgpt-web",
                DisplayName: "ChatGPT Team N2",
                MaxTabs: 2
            );
            var created = await accountService.CreateAccountAsync(createReq);

            Assert(created.MaxTabs == 2, $"Kỳ vọng MaxTabs = 2, thực tế {created.MaxTabs}");
            Assert(created.MaxConcurrentJobs == 2, $"Kỳ vọng MaxConcurrentJobs tự động đồng bộ = 2, thực tế {created.MaxConcurrentJobs}");

            // 2. Xác minh trong DB thật
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var dbAcc = await db.ProviderAccounts.FirstAsync(a => a.Id == created.Id);
                Assert(dbAcc.MaxTabs == 2, "DB phải lưu MaxTabs = 2");
                Assert(dbAcc.MaxConcurrentJobs == 2, "DB phải lưu MaxConcurrentJobs = 2");
            }

            // 3. Giả lập xác minh danh tính để có thể kích hoạt
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var acc = await db.ProviderAccounts.FirstAsync(a => a.Id == created.Id);
                acc.VerifiedIdentity = "team@chatgpt.internal";
                acc.IdentityVerifiedAtUtc = DateTime.UtcNow.ToString("o");
                await db.SaveChangesAsync();
            }

            // 4. Sửa tài khoản đổi N lên 4
            var updateReq = new UpdateProviderAccountRequest(
                Id: created.Id,
                DisplayName: "ChatGPT Team N4",
                Enabled: true,
                MaxTabs: 4,
                MaxConcurrentJobs: 4,
                PerTaskLimitsJson: null,
                ExpectedRevision: created.Revision
            );
            var updated = await accountService.UpdateAccountAsync(updateReq);

            Assert(updated.MaxTabs == 4, $"Kỳ vọng MaxTabs sau sửa = 4, thực tế {updated.MaxTabs}");
            Assert(updated.MaxConcurrentJobs == 4, $"Kỳ vọng MaxConcurrentJobs sau sửa = 4, thực tế {updated.MaxConcurrentJobs}");

            // 5. Reload lại danh sách tài khoản
            var reloadedList = await accountService.ListAccountsAsync();
            var reloadedAcc = reloadedList.First(a => a.Id == created.Id);
            Assert(reloadedAcc.MaxTabs == 4, $"Sau reload, MaxTabs phải duy trì = 4, thực tế {reloadedAcc.MaxTabs}");
            Assert(reloadedAcc.MaxConcurrentJobs == 4, "Sau reload, MaxConcurrentJobs phải = 4");
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// Kịch bản 2: N=2 với 5 job đủ điều kiện và budget đủ:
    /// Tối đa 2 job chạy trên 2 tab; còn lại chờ; một job xong thì job tiếp theo được chạy.
    /// </summary>
    public static async Task Test_AccountConcurrency_N2_With5Jobs_StreamingNextAsync()
    {
        var (dbOptions, tempDir, _, settingsService) = CreateTestContext();
        try
        {
            var now = DateTimeOffset.UtcNow.ToString("o");

            // Tạo tài khoản với N = 2 (MaxTabs = 2, MaxConcurrentJobs = 2)
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                db.BrowserProfiles.Add(new BrowserProfile
                {
                    Id = "profile_chatgpt_n2",
                    Kind = "SharedAI",
                    ProviderKey = "chatgpt-web",
                    DisplayName = "ChatGPT N2 Profile",
                    SessionStatus = "Connected",
                    OperationalState = "Stopped",
                    VisibilityState = "Hidden",
                    Lifecycle = "Active",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });

                db.ProviderAccounts.Add(new ProviderAccount
                {
                    Id = "acc-chatgpt-n2",
                    ProviderKey = "chatgpt-web",
                    ProfileId = "profile_chatgpt_n2",
                    DisplayName = "ChatGPT N2 Account",
                    VerifiedIdentity = "team@chatgpt.internal",
                    IdentityVerifiedAtUtc = now,
                    Enabled = true,
                    Lifecycle = "Active",
                    MaxConcurrentJobs = 2,
                    MaxTabs = 2,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });

                db.AppSettings.Add(new AppSetting
                {
                    Key = SettingsService.KeyScriptProvider,
                    Value = "chatgpt-web",
                    UpdatedAtUtc = now
                });

                // Tạo 5 Workspace độc lập
                for (int i = 1; i <= 5; i++)
                {
                    db.Workspaces.Add(new Workspace
                    {
                        Id = $"ws_n2_{i}",
                        Name = $"Kênh TikTok {i}",
                        Status = "Active",
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    });
                    db.Projects.Add(new Project
                    {
                        Id = $"prj_n2_{i}",
                        WorkspaceId = $"ws_n2_{i}",
                        Name = $"Dự án {i}",
                        Status = "Draft",
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    });
                }

                await db.SaveChangesAsync();
            }

            var snapshotFactory = new JobSnapshotFactory(settingsService);
            var reservation = new JobReservation(dbOptions);
            var controlledExecutor = new ControlledJobExecutor();
            var scheduler = new JobScheduler(dbOptions, reservation, controlledExecutor);
            var jobService = new JobService(dbOptions, snapshotFactory, scheduler);

            // Enqueue 5 jobs đủ điều kiện
            var jobIds = new List<string>();
            for (int i = 1; i <= 5; i++)
            {
                var res = await jobService.EnqueueAsync(new JobEnqueueRequest(
                    WorkspaceId: $"ws_n2_{i}",
                    ProjectId: $"prj_n2_{i}",
                    SceneId: null,
                    TaskType: JobTaskType.Script,
                    CustomPromptText: $"Kịch bản {i}"
                ));
                jobIds.Add(res.JobId);
            }

            // Dispatch đợt 1: Do N = 2, tối đa chỉ đúng 2 jobs được dispatch
            int dispatched1 = await scheduler.DispatchPendingJobsAsync();
            Assert(dispatched1 == 2, $"Kỳ vọng đúng 2 jobs được dispatch theo N=2, thực tế: {dispatched1}");

            // Chờ các worker chuyển trạng thái sang Running
            for (int wait = 0; wait < 100; wait++)
            {
                await using var db = new TiToAIDbContext(dbOptions);
                var runningCount = await db.Jobs.Where(j => j.State == JobState.Running).CountAsync();
                if (runningCount == 2) break;
                await Task.Delay(20);
            }

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var running = await db.Jobs.Where(j => j.State == JobState.Running).ToListAsync();
                var queued = await db.Jobs.Where(j => j.State == JobState.Queued).ToListAsync();
                Assert(running.Count == 2, $"Phải có đúng 2 jobs đang Running, thực tế {running.Count}");
                Assert(queued.Count == 3, $"3 jobs còn lại phải ở trạng thái Queued, thực tế {queued.Count}");
                Assert(queued.All(j => j.Attempts == 0), "Job chờ không được tăng attempts");

                var occupancies = await db.ActiveSlotOccupancies.ToListAsync();
                Assert(occupancies.Count == 2, $"Phải có đúng 2 slot occupancy trong DB, thực tế {occupancies.Count}");
                var slots = occupancies.Select(o => o.TabSlotId).OrderBy(s => s).ToList();
                Assert(slots[0] == "slot-1" && slots[1] == "slot-2", "Hai slot phải là slot-1 và slot-2 riêng biệt");
            }

            // Hoàn tất 1 job đang chạy
            string firstRunningId;
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                firstRunningId = (await db.Jobs.FirstAsync(j => j.State == JobState.Running)).Id;
            }
            controlledExecutor.CompleteJob(firstRunningId);

            // Chờ worker nhả slot
            for (int wait = 0; wait < 50; wait++)
            {
                await Task.Delay(20);
                await using var db = new TiToAIDbContext(dbOptions);
                var j = await db.Jobs.FirstAsync(x => x.Id == firstRunningId);
                if (j.State == JobState.Succeeded) break;
            }

            // Dispatch đợt 2: Slot vừa rảnh được cấp ngay cho job thứ 3 đang chờ
            int dispatched2 = await scheduler.DispatchPendingJobsAsync();
            Assert(dispatched2 == 1, $"Kỳ vọng đúng 1 job tiếp theo được chạy, thực tế: {dispatched2}");

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var runningCount = await db.Jobs.CountAsync(j => j.State == JobState.Running);
                var succeededCount = await db.Jobs.CountAsync(j => j.State == JobState.Succeeded);
                var queuedCount = await db.Jobs.CountAsync(j => j.State == JobState.Queued);

                Assert(runningCount == 2, $"Tiếp tục duy trì tối đa 2 jobs chạy, thực tế {runningCount}");
                Assert(succeededCount == 1, "Đã có 1 job hoàn tất");
                Assert(queuedCount == 2, $"Còn 2 jobs đang chờ, thực tế {queuedCount}");
            }
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// Kịch bản 3: Dữ liệu cũ có MaxJobs khác MaxTabs (ví dụ MaxJobs = 1, MaxTabs = 3).
    /// Hệ thống phải lấy MaxTabs (3) làm N mới, KHÔNG lấy min(1, 3) và không bị MaxJobs cũ chặn ngầm ở 1 job!
    /// </summary>
    public static async Task Test_AccountConcurrency_LegacyAccount_UsesMaxTabsAsCapAsync()
    {
        var (dbOptions, tempDir, _, settingsService) = CreateTestContext();
        try
        {
            var now = DateTimeOffset.UtcNow.ToString("o");

            // Seed tài khoản dữ liệu cũ: MaxConcurrentJobs = 1, MaxTabs = 3
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                db.BrowserProfiles.Add(new BrowserProfile
                {
                    Id = "profile_legacy_diff",
                    Kind = "SharedAI",
                    ProviderKey = "chatgpt-web",
                    DisplayName = "Legacy Profile",
                    SessionStatus = "Connected",
                    OperationalState = "Stopped",
                    VisibilityState = "Hidden",
                    Lifecycle = "Active",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });

                db.ProviderAccounts.Add(new ProviderAccount
                {
                    Id = "acc-legacy-diff",
                    ProviderKey = "chatgpt-web",
                    ProfileId = "profile_legacy_diff",
                    DisplayName = "Legacy Account Different Limits",
                    VerifiedIdentity = "legacy@chatgpt.internal",
                    IdentityVerifiedAtUtc = now,
                    Enabled = true,
                    Lifecycle = "Active",
                    MaxConcurrentJobs = 1, // Dữ liệu cũ: MaxJobs = 1
                    MaxTabs = 3,           // Dữ liệu cũ: MaxTabs = 3
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });

                db.AppSettings.Add(new AppSetting
                {
                    Key = SettingsService.KeyScriptProvider,
                    Value = "chatgpt-web",
                    UpdatedAtUtc = now
                });

                for (int i = 1; i <= 3; i++)
                {
                    db.Workspaces.Add(new Workspace
                    {
                        Id = $"ws_legacy_{i}",
                        Name = $"Kênh {i}",
                        Status = "Active",
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    });
                    db.Projects.Add(new Project
                    {
                        Id = $"prj_legacy_{i}",
                        WorkspaceId = $"ws_legacy_{i}",
                        Name = $"Dự án {i}",
                        Status = "Draft",
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    });
                }

                await db.SaveChangesAsync();
            }

            var snapshotFactory = new JobSnapshotFactory(settingsService);
            var reservation = new JobReservation(dbOptions);
            var controlledExecutor = new ControlledJobExecutor();
            var scheduler = new JobScheduler(dbOptions, reservation, controlledExecutor);
            var jobService = new JobService(dbOptions, snapshotFactory, scheduler);

            // Enqueue 3 jobs
            for (int i = 1; i <= 3; i++)
            {
                await jobService.EnqueueAsync(new JobEnqueueRequest(
                    WorkspaceId: $"ws_legacy_{i}",
                    ProjectId: $"prj_legacy_{i}",
                    SceneId: null,
                    TaskType: JobTaskType.Script,
                    CustomPromptText: $"Kịch bản legacy {i}"
                ));
            }

            // Dispatch: Nếu theo logic cũ Math.Min(1, 3) = 1 thì chỉ 1 job được chạy.
            // Nhưng theo quy tắc mới: N lấy từ MaxTabs = 3 -> Cả 3 job phải được chạy đồng thời!
            int dispatched = await scheduler.DispatchPendingJobsAsync();
            Assert(dispatched == 3, $"Kỳ vọng cả 3 jobs được dispatch theo MaxTabs=3, thực tế: {dispatched}");

            // Chờ các worker chuyển trạng thái sang Running
            for (int wait = 0; wait < 100; wait++)
            {
                await using var db = new TiToAIDbContext(dbOptions);
                var runningCount = await db.Jobs.Where(j => j.State == JobState.Running).CountAsync();
                if (runningCount == 3) break;
                await Task.Delay(20);
            }

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var running = await db.Jobs.Where(j => j.State == JobState.Running).CountAsync();
                Assert(running == 3, $"Cả 3 jobs phải đang Running đồng thời, thực tế {running}");
            }
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// Kịch bản 4: Giảm N khi đang có job chạy:
    /// - Không hủy các job đang chạy.
    /// - Slot occupancy của các job đang chạy được bảo toàn.
    /// - Ngừng nhận thêm job mới cho đến khi số job chạy giảm xuống dưới N mới.
    /// </summary>
    public static async Task Test_AccountConcurrency_ReduceNDuringRunningJobsAsync()
    {
        var (dbOptions, tempDir, _, settingsService) = CreateTestContext();
        try
        {
            var now = DateTimeOffset.UtcNow.ToString("o");

            // Tài khoản ban đầu có N = 3
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                db.BrowserProfiles.Add(new BrowserProfile
                {
                    Id = "profile_reduce_n",
                    Kind = "SharedAI",
                    ProviderKey = "chatgpt-web",
                    DisplayName = "Profile Reduce N",
                    SessionStatus = "Connected",
                    OperationalState = "Stopped",
                    VisibilityState = "Hidden",
                    Lifecycle = "Active",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });

                db.ProviderAccounts.Add(new ProviderAccount
                {
                    Id = "acc-reduce-n",
                    ProviderKey = "chatgpt-web",
                    ProfileId = "profile_reduce_n",
                    DisplayName = "Account Reduce N",
                    VerifiedIdentity = "team@chatgpt.internal",
                    IdentityVerifiedAtUtc = now,
                    Enabled = true,
                    Lifecycle = "Active",
                    MaxConcurrentJobs = 3,
                    MaxTabs = 3,
                    Revision = 1,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });

                db.AppSettings.Add(new AppSetting
                {
                    Key = SettingsService.KeyScriptProvider,
                    Value = "chatgpt-web",
                    UpdatedAtUtc = now
                });

                for (int i = 1; i <= 3; i++)
                {
                    db.Workspaces.Add(new Workspace
                    {
                        Id = $"ws_red_{i}",
                        Name = $"Kênh {i}",
                        Status = "Active",
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    });
                    db.Projects.Add(new Project
                    {
                        Id = $"prj_red_{i}",
                        WorkspaceId = $"ws_red_{i}",
                        Name = $"Dự án {i}",
                        Status = "Draft",
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    });
                }

                await db.SaveChangesAsync();
            }

            var snapshotFactory = new JobSnapshotFactory(settingsService);
            var reservation = new JobReservation(dbOptions);
            var controlledExecutor = new ControlledJobExecutor();
            var scheduler = new JobScheduler(dbOptions, reservation, controlledExecutor);
            var jobService = new JobService(dbOptions, snapshotFactory, scheduler);

            // Enqueue 2 jobs ban đầu
            var j1 = await jobService.EnqueueAsync(new JobEnqueueRequest($"ws_red_1", $"prj_red_1", null, JobTaskType.Script, CustomPromptText: "Task 1"));
            var j2 = await jobService.EnqueueAsync(new JobEnqueueRequest($"ws_red_2", $"prj_red_2", null, JobTaskType.Script, CustomPromptText: "Task 2"));

            // Dispatch: cả 2 jobs chạy
            int d1 = await scheduler.DispatchPendingJobsAsync();
            Assert(d1 == 2, "Cả 2 jobs ban đầu phải chạy");

            // Chờ worker chuyển trạng thái sang Running
            for (int wait = 0; wait < 100; wait++)
            {
                await using var db = new TiToAIDbContext(dbOptions);
                var runningCount = await db.Jobs.Where(j => j.State == JobState.Running).CountAsync();
                if (runningCount == 2) break;
                await Task.Delay(20);
            }

            // Giảm N từ 3 xuống 1 qua ProviderAccountService
            var accountService = new ProviderAccountService(dbOptions, new FakeProfileIdentityResolver());
            await accountService.UpdateAccountAsync(new UpdateProviderAccountRequest(
                Id: "acc-reduce-n",
                DisplayName: "Account Reduce N",
                Enabled: true,
                MaxTabs: 1, // Giảm N xuống 1
                MaxConcurrentJobs: 1,
                PerTaskLimitsJson: null,
                ExpectedRevision: 1
            ));

            // Kiểm tra: 2 job đang chạy KHÔNG bị hủy, slot occupancy giữ nguyên
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var running = await db.Jobs.Where(j => j.State == JobState.Running).ToListAsync();
                Assert(running.Count == 2, $"Giảm N không được hủy job đang chạy; phải còn 2 running, thực tế {running.Count}");

                var occs = await db.ActiveSlotOccupancies.CountAsync(o => o.ProviderAccountId == "acc-reduce-n");
                Assert(occs == 2, $"Slot occupancy phải giữ nguyên 2, thực tế {occs}");
            }

            // Enqueue job thứ 3
            var j3 = await jobService.EnqueueAsync(new JobEnqueueRequest($"ws_red_3", $"prj_red_3", null, JobTaskType.Script, CustomPromptText: "Task 3"));

            // Thử dispatch: Không được nhận thêm vì số đang chạy (2) >= N mới (1)
            int d2 = await scheduler.DispatchPendingJobsAsync();
            Assert(d2 == 0, $"Không được nhận thêm job khi số đang chạy >= N mới; dispatched: {d2}");

            // Hoàn tất 1 job (j1): còn 1 job đang chạy
            controlledExecutor.CompleteJob(j1.JobId);
            for (int wait = 0; wait < 50; wait++)
            {
                await Task.Delay(20);
                await using var db = new TiToAIDbContext(dbOptions);
                var job = await db.Jobs.FirstAsync(x => x.Id == j1.JobId);
                if (job.State == JobState.Succeeded) break;
            }

            // Dispatch lại: Vẫn không nhận thêm vì 1 >= 1
            int d3 = await scheduler.DispatchPendingJobsAsync();
            Assert(d3 == 0, $"Còn 1 job đang chạy = N mới (1), vẫn chưa được nhận thêm; dispatched: {d3}");

            // Hoàn tất job thứ 2 (j2): số đang chạy = 0 < N mới (1)
            controlledExecutor.CompleteJob(j2.JobId);
            for (int wait = 0; wait < 50; wait++)
            {
                await Task.Delay(20);
                await using var db = new TiToAIDbContext(dbOptions);
                var job = await db.Jobs.FirstAsync(x => x.Id == j2.JobId);
                if (job.State == JobState.Succeeded) break;
            }

            // Dispatch lần này: số đang chạy = 0 < 1 -> job 3 được cấp slot chạy!
            int d4 = await scheduler.DispatchPendingJobsAsync();
            Assert(d4 == 1, $"Job 3 phải được cấp slot khi số đang chạy < N mới; dispatched: {d4}");

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var j3Db = await db.Jobs.FirstAsync(x => x.Id == j3.JobId);
                Assert(j3Db.State == JobState.Running, "Job 3 phải chuyển sang Running");
            }
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// Kịch bản 5: Vòng đời browser:
    /// - Mở app không có browser automation chạy.
    /// - Có job đủ điều kiện reserve -> browser automation mở chạy ẩn.
    /// - Hết job -> browser đóng và giải phóng tài nguyên.
    /// - Chạy job tiếp theo -> browser mở lại và vẫn dùng được phiên đã lưu.
    /// </summary>
    public static async Task Test_BrowserLifecycle_FullLifecycleWithSavedSessionAsync()
    {
        var (dbOptions, tempDir, _, settingsService) = CreateTestContext();
        try
        {
            var now = DateTimeOffset.UtcNow.ToString("o");
            string profileId = "profile_lifecycle_test";

            // Tạo profile và account
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                db.BrowserProfiles.Add(new BrowserProfile
                {
                    Id = profileId,
                    Kind = "SharedAI",
                    ProviderKey = "chatgpt-web",
                    DisplayName = "Lifecycle Profile",
                    SessionStatus = "Connected",
                    OperationalState = "Stopped",
                    VisibilityState = "Hidden",
                    Lifecycle = "Active",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });

                db.ProviderAccounts.Add(new ProviderAccount
                {
                    Id = "acc-lifecycle",
                    ProviderKey = "chatgpt-web",
                    ProfileId = profileId,
                    DisplayName = "Lifecycle Account",
                    VerifiedIdentity = "team@chatgpt.internal",
                    IdentityVerifiedAtUtc = now,
                    Enabled = true,
                    Lifecycle = "Active",
                    MaxConcurrentJobs = 2,
                    MaxTabs = 2,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });

                db.AppSettings.Add(new AppSetting
                {
                    Key = SettingsService.KeyScriptProvider,
                    Value = "chatgpt-web",
                    UpdatedAtUtc = now
                });

                db.Workspaces.Add(new Workspace { Id = "ws_lc", Name = "Kênh LC", Status = "Active", CreatedAtUtc = now, UpdatedAtUtc = now });
                db.Projects.Add(new Project { Id = "prj_lc", WorkspaceId = "ws_lc", Name = "Dự án LC", Status = "Draft", CreatedAtUtc = now, UpdatedAtUtc = now });

                await db.SaveChangesAsync();
            }

            var profileRegistry = new ProfileRegistry(() => new TiToAIDbContext(dbOptions), tempDir);
            var profileOwner = new ProfileOwner(pid => Path.Combine(tempDir, "profiles", pid));
            var fakeLauncher = new TrackingPersistentLauncher();
            var processController = new PlaywrightBrowserController(
                profileRegistry,
                profileOwner,
                customPersistentLauncher: fakeLauncher.LaunchAsync);

            // 1. Kiểm tra: Khi mở app / load account, KHÔNG có browser automation nào chạy!
            Assert(!processController.IsRunning(profileId), "Mở app: tuyệt đối không tự khởi chạy browser.");

            var tabPool = new TabPool();
            var loginCoordinator = new LoginCoordinator(profileRegistry, processController);
            var gatekeeper = new HostBrowserGatekeeper(profileRegistry, loginCoordinator, tabPool, processController);

            var snapshotFactory = new JobSnapshotFactory(settingsService);
            var reservation = new JobReservation(dbOptions);
            var controlledExecutor = new ControlledJobExecutor();
            var scheduler = new JobScheduler(dbOptions, reservation, controlledExecutor, gatekeeper);
            var jobService = new JobService(dbOptions, snapshotFactory, scheduler);

            // 2. Enqueue Job 1
            var j1 = await jobService.EnqueueAsync(new JobEnqueueRequest("ws_lc", "prj_lc", null, JobTaskType.Script, CustomPromptText: "Task LC 1"));

            // Dispatch Job 1: Reserve thành công -> Browser automation mở chạy ẩn!
            int d1 = await scheduler.DispatchPendingJobsAsync();
            Assert(d1 == 1, "Job 1 phải được dispatch");

            for (int wait = 0; wait < 100; wait++)
            {
                if (processController.IsRunning(profileId) && tabPool.GetActiveJobCount(profileId) == 1) break;
                await Task.Delay(20);
            }

            Assert(processController.IsRunning(profileId), "Khi có job chạy: browser automation phải mở.");
            Assert(fakeLauncher.LastLaunchWasHeadless == true, "Browser automation phải chạy ẩn (isHeadless = true).");
            Assert(tabPool.GetActiveJobCount(profileId) == 1, "Tab pool phải có 1 tab đang mở cho Job 1.");

            // 3. Hoàn tất Job 1
            controlledExecutor.CompleteJob(j1.JobId);
            for (int wait = 0; wait < 50; wait++)
            {
                await Task.Delay(20);
                await using var db = new TiToAIDbContext(dbOptions);
                var j = await db.Jobs.FirstAsync(x => x.Id == j1.JobId);
                if (j.State == JobState.Succeeded) break;
            }

            // Chờ browser context đóng khi idle
            for (int wait = 0; wait < 50; wait++)
            {
                if (!processController.IsRunning(profileId)) break;
                await Task.Delay(20);
            }

            Assert(!processController.IsRunning(profileId), "Khi hết job: browser phải đóng và giải phóng tài nguyên, không giữ chạy nền.");

            // Kiểm tra trạng thái phiên trong ProfileRegistry: Đóng browser không làm mất phiên đăng nhập!
            var pState = await profileRegistry.GetProfileAsync(profileId);
            Assert(pState != null && pState.SessionStatus == "Connected", "Đóng browser phải bảo toàn phiên đăng nhập (SessionStatus vẫn là Connected).");

            // 4. Enqueue Job 2 tiếp theo: Browser automation mở lại và tiếp tục dùng phiên đã lưu
            var j2 = await jobService.EnqueueAsync(new JobEnqueueRequest("ws_lc", "prj_lc", null, JobTaskType.Script, CustomPromptText: "Task LC 2"));
            int d2 = await scheduler.DispatchPendingJobsAsync();
            Assert(d2 == 1, "Job 2 phải được dispatch");

            for (int wait = 0; wait < 100; wait++)
            {
                if (processController.IsRunning(profileId)) break;
                await Task.Delay(20);
            }

            Assert(processController.IsRunning(profileId), "Job tiếp theo: browser mở lại thành công.");
            Assert(fakeLauncher.LaunchCount == 2, "Đã khởi chạy browser lần 2 từ cùng profile.");

            controlledExecutor.CompleteJob(j2.JobId);
            for (int wait = 0; wait < 50; wait++)
            {
                await Task.Delay(20);
                if (!processController.IsRunning(profileId)) break;
            }
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// Kịch bản 6: An toàn khi job mới đến trong lúc browser đang đóng (Closing):
    /// Không bao giờ tạo 2 browser cùng sở hữu 1 profile.
    /// </summary>
    public static async Task Test_BrowserLifecycle_JobArrivalDuringClosing_SafeAsync()
    {
        var (dbOptions, tempDir, _, _) = CreateTestContext();
        try
        {
            var profileRegistry = new ProfileRegistry(() => new TiToAIDbContext(dbOptions), tempDir);
            var profileOwner = new ProfileOwner(pid => Path.Combine(tempDir, "profiles", pid));
            var fakeLauncher = new TrackingPersistentLauncher();
            var processController = new PlaywrightBrowserController(
                profileRegistry,
                profileOwner,
                customPersistentLauncher: fakeLauncher.LaunchAsync);

            string profileId = "profile_closing_safety";

            // Tạo profile trong registry
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                db.BrowserProfiles.Add(new BrowserProfile
                {
                    Id = profileId,
                    Kind = "SharedAI",
                    ProviderKey = "chatgpt-web",
                    DisplayName = "Closing Safety Profile",
                    SessionStatus = "Connected",
                    OperationalState = "Stopped",
                    VisibilityState = "Hidden",
                    Lifecycle = "Active",
                    CreatedAtUtc = DateTime.UtcNow.ToString("o"),
                    UpdatedAtUtc = DateTime.UtcNow.ToString("o")
                });
                await db.SaveChangesAsync();
            }

            // Khởi chạy browser lần 1
            await processController.LaunchPersistentContextAsync(profileId, isHeadless: true);
            Assert(processController.IsRunning(profileId), "Browser lần 1 đang chạy.");

            // Bắt đầu đóng browser và đồng thời yêu cầu mở lại
            var closeTask = processController.CloseContextAsync(profileId);
            var launchTask = processController.LaunchPersistentContextAsync(profileId, isHeadless: true);

            await Task.WhenAll(closeTask, launchTask);

            // Xác minh: Luôn giải quyết xác định và an toàn, không có lỗi xung đột owner lock
            Assert(processController.IsRunning(profileId), "Browser sau đó mở lại an toàn.");
            Assert(fakeLauncher.LaunchCount == 2, "Đã launch đúng 2 lần tuần tự.");

            await processController.CloseContextAsync(profileId);
            Assert(!processController.IsRunning(profileId), "Browser đã đóng an toàn.");
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    /// <summary>
    /// Kịch bản 7: Không đóng browser khi còn job đang chạy hoặc job chưa rõ kết quả đối soát:
    /// Job AwaitingUser có SideEffectSubmitted == 1 ngăn cản đóng browser.
    /// </summary>
    public static async Task Test_BrowserLifecycle_DoesNotCloseWhenJobActiveOrUncertainAsync()
    {
        var (dbOptions, tempDir, _, _) = CreateTestContext();
        try
        {
            var profileRegistry = new ProfileRegistry(() => new TiToAIDbContext(dbOptions), tempDir);
            var profileOwner = new ProfileOwner(pid => Path.Combine(tempDir, "profiles", pid));
            var fakeLauncher = new TrackingPersistentLauncher();
            var processController = new PlaywrightBrowserController(
                profileRegistry,
                profileOwner,
                customPersistentLauncher: fakeLauncher.LaunchAsync);

            string profileId = "profile_uncertain_keep";

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                db.BrowserProfiles.Add(new BrowserProfile
                {
                    Id = profileId,
                    Kind = "SharedAI",
                    ProviderKey = "chatgpt-web",
                    DisplayName = "Uncertain Profile",
                    SessionStatus = "Connected",
                    OperationalState = "Stopped",
                    VisibilityState = "Hidden",
                    Lifecycle = "Active",
                    CreatedAtUtc = DateTime.UtcNow.ToString("o"),
                    UpdatedAtUtc = DateTime.UtcNow.ToString("o")
                });
                await db.SaveChangesAsync();
            }

            var tabPool = new TabPool();
            var loginCoordinator = new LoginCoordinator(profileRegistry, processController);
            var checkpointCoordinator = new BrowserSessionCheckpointCoordinator(
                profileRegistry,
                loginCoordinator,
                null,
                processController,
                tabPool,
                () => new TiToAIDbContext(dbOptions));

            var gatekeeper = new HostBrowserGatekeeper(
                profileRegistry,
                loginCoordinator,
                tabPool,
                processController,
                checkpointCoordinator);

            // Mở browser context
            await processController.LaunchPersistentContextAsync(profileId, isHeadless: true);

            // Trường hợp 1: Có job đang theo dõi với SideEffectSubmitted == 1 ở trạng thái AwaitingUser
            checkpointCoordinator.TrackJob(profileId, "job_uncertain_1", sideEffectSubmitted: true, state: JobState.AwaitingUser);

            Assert(!gatekeeper.IsProfileIdle(profileId), "Job AwaitingUser với side effect chưa đối soát -> Profile không được coi là idle.");

            gatekeeper.CloseProfileIfIdle(profileId);
            await Task.Delay(50);
            Assert(processController.IsRunning(profileId), "Tuyệt đối không được đóng browser khi có job uncertain cần đối soát.");

            // Trường hợp 2: Sau khi job đối soát xong (untrack)
            checkpointCoordinator.UntrackJob(profileId, "job_uncertain_1");
            Assert(gatekeeper.IsProfileIdle(profileId), "Khi không còn job nào -> Profile trở thành idle.");

            gatekeeper.CloseProfileIfIdle(profileId);
            for (int wait = 0; wait < 50; wait++)
            {
                if (!processController.IsRunning(profileId)) break;
                await Task.Delay(20);
            }

            Assert(!processController.IsRunning(profileId), "Khi thực sự idle: browser được đóng an toàn.");
        }
        finally
        {
            CleanupTestContext(tempDir);
        }
    }

    private sealed class TrackingPersistentLauncher
    {
        public int LaunchCount { get; private set; }
        public bool? LastLaunchWasHeadless { get; private set; }

        public Task<IBrowserContext> LaunchAsync(string userDataDir, BrowserTypeLaunchPersistentContextOptions options)
        {
            LaunchCount++;
            LastLaunchWasHeadless = options.Headless;
            var proxy = DispatchProxy.Create<IBrowserContext, BrowserSmokeTests.MockBrowserContextProxy>();
            return Task.FromResult(proxy);
        }
    }

    private sealed class FakeProfileIdentityResolver : IProfileIdentityResolver
    {
        private readonly string? _identity;
        public FakeProfileIdentityResolver(string? identity = null) => _identity = identity;

        public Task<ProfileIdentityResolutionResult> ResolveProfileIdentityAsync(string profileId, string provider, CancellationToken cancellationToken = default)
        {
            if (_identity != null)
            {
                return Task.FromResult(ProfileIdentityResolutionResult.Success(profileId, provider, _identity, "Connected"));
            }
            return Task.FromResult(ProfileIdentityResolutionResult.Failed("Chưa xác minh danh tính."));
        }
    }

    private sealed class ControlledJobExecutor : IJobExecutor
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _tcsMap = new();

        public TaskCompletionSource GetOrAddJobTcs(string jobId) =>
            _tcsMap.GetOrAdd(jobId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        public void CompleteJob(string jobId)
        {
            var tcs = GetOrAddJobTcs(jobId);
            tcs.TrySetResult();
        }

        public async Task<JobExecutionResult> ExecuteAsync(
            JobExecutionContext context,
            Func<Task> onPreSideEffectCommitAsync,
            Func<int, string, Task>? onProgressAsync,
            CancellationToken cancellationToken)
        {
            var tcs = GetOrAddJobTcs(context.JobId);
            await tcs.Task.WaitAsync(cancellationToken);
            return new JobExecutionResult(Success: true, ResultReference: $"asset-{context.JobId}");
        }
    }
}
