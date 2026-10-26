using System.IO;
using System.Reflection;
using System.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using TiToAI.Browser;
using TiToAI.Browser.Abstractions;
using TiToAI.Browser.Models;
using TiToAI.Contracts.Accounts;
using TiToAI.Contracts.Browser;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Persistence.Entities;

namespace TiToAI.Desktop.SmokeChecks;

/// <summary>
/// Bộ kiểm thử khói (Smoke Tests) cho Task T13:
/// Browser Profile Registry, Single Instance Profile Lock, Tab Pool, Login Coordinator,
/// Quiescing / Drain Handoff, Crash Recovery, Session Probe, Playwright Process Controller & Login Handoff Verification.
/// </summary>
public static class BrowserSmokeTests
{
    private static async Task<(SqliteConnection Connection, Func<TiToAIDbContext> Factory)> CreateInMemoryDbContextFactoryAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TiToAIDbContext>()
            .UseSqlite(connection)
            .Options;

        using (var initialDb = new TiToAIDbContext(options))
        {
            await initialDb.Database.EnsureCreatedAsync();
        }

        return (connection, () => new TiToAIDbContext(options));
    }

    /// <summary>
    /// Test 1: Đảm bảo đường dẫn profile an toàn, không chứa email/username và chặn directory traversal.
    /// </summary>
    public static async Task Test_ProfileRegistry_PathSafety_NoEmailOrTraversal()
    {
        var (connection, factory) = await CreateInMemoryDbContextFactoryAsync();
        await using (connection)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "titoai_test_profiles_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var registry = new ProfileRegistry(factory, tempDir);

                // Đường dẫn chuẩn
                string sharedDir = registry.GetUserDataDir("shared_ai", BrowserProfileKind.SharedAI);
                Assert(Path.GetFullPath(sharedDir) == Path.Combine(tempDir, "shared_ai"), "Đường dẫn Shared AI không khớp.");

                string museDir = registry.GetUserDataDir("museai_channel1", BrowserProfileKind.MuseAI);
                Assert(Path.GetFullPath(museDir) == Path.Combine(tempDir, "museai_channel1"), "Đường dẫn MuseAI không khớp.");

                // Chặn Profile ID chứa email
                bool emailBlocked = false;
                try
                {
                    registry.GetUserDataDir("user@gmail.com", BrowserProfileKind.MuseAI);
                }
                catch (SecurityException)
                {
                    emailBlocked = true;
                }
                Assert(emailBlocked, "Profile ID chứa email phải bị chặn bằng SecurityException.");

                // Chặn Directory Traversal
                bool traversalBlocked = false;
                try
                {
                    registry.GetUserDataDir("../../../Windows/System32", BrowserProfileKind.SharedAI);
                }
                catch (SecurityException)
                {
                    traversalBlocked = true;
                }
                Assert(traversalBlocked, "Profile ID chứa ký tự traversal phải bị chặn bằng SecurityException.");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }
    }

    /// <summary>
    /// Test 2: Đảm bảo Single-Instance Profile Lock (In-process và File Lock).
    /// </summary>
    public static async Task Test_SingleInstanceProfileLock_InProcessAndCrossProcess()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "titoai_lock_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var owner = new ProfileOwner(profileId => Path.Combine(tempDir, profileId));
            string profileId = "shared_ai";

            // Owner 1 chiếm lock
            await using var lock1 = await owner.AcquireOwnerLockAsync(profileId, "JobWorker-1", TimeSpan.FromSeconds(2));
            Assert(owner.IsLocked(profileId), "Profile phải ở trạng thái đã bị khóa.");
            Assert(owner.GetCurrentOwnerTag(profileId) == "JobWorker-1", "Tag owner phải là JobWorker-1.");

            // Owner 2 cố gắng chiếm lock của cùng profileId -> phải ném TimeoutException
            bool lockBlocked = false;
            try
            {
                await owner.AcquireOwnerLockAsync(profileId, "JobWorker-2", TimeSpan.FromMilliseconds(100));
            }
            catch (TimeoutException)
            {
                lockBlocked = true;
            }
            Assert(lockBlocked, "Owner 2 chiếm cùng profile phải bị TimeoutException.");

            // Kiểm tra file lock cấp hệ điều hành không cho phép process/stream khác mở
            string lockFilePath = Path.Combine(tempDir, profileId, ".titoai_lock");
            Assert(File.Exists(lockFilePath), "Tệp .titoai_lock phải tồn tại trên đĩa.");

            bool fileLocked = false;
            try
            {
                using var stream = new FileStream(lockFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                fileLocked = true;
            }
            Assert(fileLocked, "File .titoai_lock phải bị khóa cấp HĐH (IOException).");

            // Giải phóng lock 1
            await lock1.DisposeAsync();
            Assert(!owner.IsLocked(profileId), "Profile phải mở khóa sau khi Dispose.");

            // Sau khi giải phóng, Owner 2 có thể chiếm thành công
            await using var lock2 = await owner.AcquireOwnerLockAsync(profileId, "JobWorker-2", TimeSpan.FromSeconds(1));
            Assert(owner.IsLocked(profileId), "Owner 2 phải chiếm được khóa sau khi Owner 1 giải phóng.");
            Assert(owner.GetCurrentOwnerTag(profileId) == "JobWorker-2", "Tag owner phải là JobWorker-2.");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    /// <summary>
    /// Test 3: Tab Pool quản lý 3 role tabs cho Shared AI, cách ly ngữ cảnh, tối đa 1 job/tab.
    /// </summary>
    public static async Task Test_TabPool_SharedAi_QuotaAndRoleIsolation()
    {
        var pool = new TabPool();
        string profileId = "shared_ai";

        // Chặn role không hợp lệ cho Shared AI
        bool invalidRoleBlocked = false;
        try
        {
            await pool.AcquireTabAsync(profileId, "CustomInvalidRole", "job-999");
        }
        catch (ArgumentException)
        {
            invalidRoleBlocked = true;
        }
        Assert(invalidRoleBlocked, "Role không nằm trong danh sách chuẩn của Shared AI phải bị từ chối.");

        // Cấp phát tab Script cho job-1
        await using var tabScript = await pool.AcquireTabAsync(profileId, SharedAiTabRole.Script, "job-1");
        Assert(pool.IsRoleActive(profileId, SharedAiTabRole.Script), "Tab Script phải đang hoạt động.");
        Assert(pool.GetActiveJobCount(profileId) == 1, "ActiveJobCount phải là 1.");

        // Cố tình cấp phát thêm job-2 vào tab Script -> phải ném InvalidOperationException (1 job/tab)
        bool concurrentTabBlocked = false;
        try
        {
            await pool.AcquireTabAsync(profileId, SharedAiTabRole.Script, "job-2");
        }
        catch (InvalidOperationException)
        {
            concurrentTabBlocked = true;
        }
        Assert(concurrentTabBlocked, "Không được phép cấp phát 2 job đồng thời trên cùng một tab.");

        // Cấp phát tiếp tab Image và VideoPrompt
        await using var tabImage = await pool.AcquireTabAsync(profileId, SharedAiTabRole.Image, "job-2");
        await using var tabVideo = await pool.AcquireTabAsync(profileId, SharedAiTabRole.VideoPrompt, "job-3");

        Assert(pool.GetActiveJobCount(profileId) == 3, "ActiveJobCount phải là 3 khi cả 3 role tab đều có job.");
        var activeTabs = pool.GetActiveTabs(profileId);
        Assert(activeTabs.Count == 3, "Danh sách active tabs phải có 3 phần tử.");

        // Giải phóng tab Script
        await tabScript.DisposeAsync();
        Assert(!pool.IsRoleActive(profileId, SharedAiTabRole.Script), "Tab Script phải được giải phóng.");
        Assert(pool.GetActiveJobCount(profileId) == 2, "ActiveJobCount phải giảm về 2.");

        // Cấp phát lại tab Script cho job mới sau khi đã giải phóng
        await using var tabScriptNew = await pool.AcquireTabAsync(profileId, SharedAiTabRole.Script, "job-4");
        Assert(pool.IsRoleActive(profileId, SharedAiTabRole.Script), "Tab Script phải được cấp phát lại thành công.");
    }

    /// <summary>
    /// Test 4: Global One-Visible-Login Rule và hàng đợi VisibleWaiting.
    /// </summary>
    public static async Task Test_LoginCoordinator_OneVisibleLoginRule_AndQueue()
    {
        var (connection, factory) = await CreateInMemoryDbContextFactoryAsync();
        await using (connection)
        {
            var registry = new ProfileRegistry(factory);
            await registry.EnsureProfileAsync("shared_ai", BrowserProfileKind.SharedAI);
            await registry.EnsureProfileAsync("museai_channel1", BrowserProfileKind.MuseAI);

            using var coordinator = new LoginCoordinator(registry);

            // Profile 1 yêu cầu login -> nhận slot hiển thị
            var res1 = await coordinator.RequestLoginAsync("shared_ai");
            Assert(!res1.Queued, "Profile 1 không bị xếp hàng.");
            Assert(res1.VisibilityState == BrowserVisibilityState.VisibleActive, "Profile 1 phải nhận VisibleActive.");
            Assert(res1.CurrentHolder == "shared_ai", "CurrentHolder phải là shared_ai.");
            Assert(coordinator.IsVisibleLoginActive, "IsVisibleLoginActive phải là true.");

            // Profile 2 yêu cầu login trong khi slot đang bận -> nhận VisibleWaiting
            var res2 = await coordinator.RequestLoginAsync("museai_channel1");
            Assert(res2.Queued, "Profile 2 phải bị xếp hàng vào hàng đợi.");
            Assert(res2.VisibilityState == BrowserVisibilityState.VisibleWaiting, "Profile 2 phải nhận VisibleWaiting.");
            Assert(res2.CurrentHolder == "shared_ai", "CurrentHolder vẫn là shared_ai.");

            // Profile 1 đóng cửa sổ login
            await coordinator.CloseLoginWindowAsync("shared_ai", BrowserReasonCode.AuthClosed);
            Assert(!coordinator.IsVisibleLoginActive, "Slot hiển thị phải được giải phóng.");

            // Profile 2 có thể chiếm slot sau khi Profile 1 đóng
            bool acquired = await coordinator.WaitForVisibleSlotAsync("museai_channel1", TimeSpan.FromSeconds(1));
            Assert(acquired, "Profile 2 phải chiếm được slot hiển thị.");
            Assert(coordinator.CurrentVisibleProfileId == "museai_channel1", "CurrentVisibleProfileId phải là museai_channel1.");
        }
    }

    /// <summary>
    /// Test 5: Người dùng đóng cửa sổ đăng nhập -> phát auth_closed và tuyệt đối không tự mở lại.
    /// </summary>
    public static async Task Test_LoginCoordinator_UserClosesWindow_AuthClosedReasonCode_NoAutoReopen()
    {
        var (connection, factory) = await CreateInMemoryDbContextFactoryAsync();
        await using (connection)
        {
            var registry = new ProfileRegistry(factory);
            await registry.EnsureProfileAsync("shared_ai", BrowserProfileKind.SharedAI);

            BrowserProfileStateChangedPayload? capturedPayload = null;
            registry.ProfileStateChanged += payload =>
            {
                capturedPayload = payload;
                return Task.CompletedTask;
            };

            using var coordinator = new LoginCoordinator(registry);

            await coordinator.RequestLoginAsync("shared_ai");
            Assert(coordinator.CurrentVisibleProfileId == "shared_ai", "shared_ai phải đang giữ cửa sổ hiển thị.");

            // Người dùng đóng cửa sổ
            await coordinator.CloseLoginWindowAsync("shared_ai", BrowserReasonCode.AuthClosed);

            Assert(!coordinator.IsVisibleLoginActive, "Cửa sổ phải được đóng.");
            Assert(coordinator.CurrentVisibleProfileId == null, "Không còn profile nào giữ slot hiển thị.");

            var p = await registry.GetProfileAsync("shared_ai");
            Assert(p?.VisibilityState == BrowserVisibilityState.Hidden, "Profile phải chuyển về Hidden.");
            Assert(p?.OperationalState == BrowserOperationalState.Stopped, "Profile phải chuyển về Stopped.");

            Assert(capturedPayload != null, "Phải phát sự kiện ProfileStateChanged.");
            Assert(capturedPayload?.ReasonCode == BrowserReasonCode.AuthClosed, "ReasonCode phải là auth_closed.");
        }
    }

    /// <summary>
    /// Test 6: Quy trình Quiescing và Drain an toàn cho Shared AI (dừng tab chưa submit, bảo toàn in-flight).
    /// </summary>
    public static async Task Test_QuiescingAndDrain_SharedAi_SafeRollbackPendingTabs()
    {
        var (connection, factory) = await CreateInMemoryDbContextFactoryAsync();
        await using (connection)
        {
            var registry = new ProfileRegistry(factory);
            await registry.EnsureProfileAsync("shared_ai", BrowserProfileKind.SharedAI);

            var pool = new TabPool();
            using var loginCoord = new LoginCoordinator(registry);
            var handoff = new BrowserHandoffCoordinator(registry, pool, loginCoord, factory);

            string profileId = "shared_ai";

            // Tab 1: Chưa submit side-effect (đang đọc/chuẩn bị)
            var tab1 = await pool.AcquireTabAsync(profileId, SharedAiTabRole.Script, "job-read-1");
            tab1.IsSideEffectSubmitted = false;

            // Tab 2: Đã submit side-effect (đang chờ kết quả từ AI website)
            var tab2 = await pool.AcquireTabAsync(profileId, SharedAiTabRole.Image, "job-submitted-2");
            tab2.IsSideEffectSubmitted = true;

            Assert(pool.GetActiveJobCount(profileId) == 2, "Ban đầu có 2 tab active.");

            // Bước 1: Pause Dispatch
            await handoff.PauseDispatchAsync(profileId);
            Assert(handoff.IsDispatchPaused(profileId), "Cờ PauseDispatch phải bật.");

            // Bước 2: Drain Pending Tabs (dừng an toàn tab chưa submit)
            var drainedJobs = new List<string>();
            await handoff.DrainPendingTabsAsync(profileId, jobId =>
            {
                drainedJobs.Add(jobId);
                return Task.CompletedTask;
            });

            Assert(drainedJobs.Contains("job-read-1"), "Job-read-1 phải được chuyển giao drain.");
            Assert(!pool.IsRoleActive(profileId, SharedAiTabRole.Script), "Tab Script chưa submit phải được release ngay.");
            Assert(pool.IsRoleActive(profileId, SharedAiTabRole.Image), "Tab Image đã submit side effect phải tiếp tục giữ.");
            Assert(pool.GetActiveJobCount(profileId) == 1, "Chỉ còn 1 tab đang in-flight.");

            // Giải phóng nốt tab đã submit sau khi có kết quả
            await tab2.DisposeAsync();
            Assert(pool.GetActiveJobCount(profileId) == 0, "Toàn bộ tab đã hoàn tất.");

            // Bước 4: Prepare Handoff to Visible Login
            await handoff.PrepareHandoffToVisibleLoginAsync(profileId, "job-submitted-2", BrowserReasonCode.SessionExpired);

            // Kiểm tra Checkpoint được lưu bền vững trong SQLite
            await using (var db = factory())
            {
                var checkpoint = await db.BrowserHandoffCheckpoints
                    .FirstOrDefaultAsync(c => c.ProfileId == profileId && c.ResolvedAtUtc == null);
                Assert(checkpoint != null, "Phải có checkpoint handoff chưa resolved trong SQLite.");
                Assert(checkpoint?.ReasonCode == BrowserReasonCode.SessionExpired, "ReasonCode checkpoint không khớp.");
            }

            // Giả lập trạng thái session đã kết nối hợp lệ sau khi người dùng đăng nhập thành công
            await registry.UpdateProfileStateAsync(profileId, sessionStatus: BrowserSessionStatus.Connected);

            // Hoàn tất handoff sau khi đăng nhập thành công và probe xác nhận Connected
            await handoff.CompleteLoginHandoffAsync(profileId, authSuccess: true);

            await using (var db = factory())
            {
                var checkpoint = await db.BrowserHandoffCheckpoints
                    .FirstOrDefaultAsync(c => c.ProfileId == profileId);
                Assert(checkpoint?.ResolvedAtUtc != null, "Checkpoint phải được đánh dấu resolved.");
            }

            var updatedProfile = await registry.GetProfileAsync(profileId);
            Assert(updatedProfile?.VisibilityState == BrowserVisibilityState.Hidden, "Profile phải về Hidden sau handoff.");
            Assert(updatedProfile?.OperationalState == BrowserOperationalState.Idle, "Profile phải về Idle.");
            Assert(updatedProfile?.SessionStatus == BrowserSessionStatus.Connected, "SessionStatus phải là Connected.");
        }
    }

    /// <summary>
    /// Test 7: Phục hồi sau sự cố tại Startup (Reconcile Checkpoints & Dirty Profiles).
    /// </summary>
    public static async Task Test_CrashRecovery_ReconcileCheckpointsAtStartup()
    {
        var (connection, factory) = await CreateInMemoryDbContextFactoryAsync();
        await using (connection)
        {
            string now = DateTime.UtcNow.ToString("o");

            // Giả lập trạng thái SQLite trước khi crash: profile đang Running/VisibleActive, checkpoint chưa resolved
            await using (var db = factory())
            {
                db.BrowserProfiles.Add(new BrowserProfile
                {
                    Id = "shared_ai",
                    Kind = BrowserProfileKind.SharedAI,
                    VisibilityState = BrowserVisibilityState.VisibleActive,
                    OperationalState = BrowserOperationalState.Running,
                    SessionStatus = BrowserSessionStatus.Connected,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });

                db.BrowserHandoffCheckpoints.Add(new BrowserHandoffCheckpoint
                {
                    Id = Guid.NewGuid().ToString(),
                    ProfileId = "shared_ai",
                    ActiveJobId = "job-crashed",
                    PreviousState = BrowserOperationalState.Running,
                    TargetVisibilityState = BrowserVisibilityState.VisibleActive,
                    ReasonCode = BrowserReasonCode.SessionExpired,
                    CreatedAtUtc = now,
                    ResolvedAtUtc = null
                });

                await db.SaveChangesAsync();
            }

            var registry = new ProfileRegistry(factory);

            // Ứng dụng khởi động lại -> gọi ReconcileCheckpointsAtStartupAsync
            await registry.ReconcileCheckpointsAtStartupAsync();

            await using (var db = factory())
            {
                var checkpoint = await db.BrowserHandoffCheckpoints.FirstAsync();
                Assert(checkpoint.ResolvedAtUtc != null, "Checkpoint treo phải được giải quyết tại startup.");

                var profile = await db.BrowserProfiles.FirstAsync();
                Assert(profile.VisibilityState == BrowserVisibilityState.Hidden, "VisibilityState phải được reset về Hidden.");
                Assert(profile.OperationalState == BrowserOperationalState.Stopped, "OperationalState phải được reset về Stopped.");
                // Không phục hồi Connected từ metadata cũ nếu tiến trình đã chết!
                Assert(profile.SessionStatus == BrowserSessionStatus.Unverified, "SessionStatus phải chuyển về Unverified.");
            }
        }
    }

    /// <summary>
    /// Test 8: Session Probe & IProfileIdentityResolver - không bao giờ fake PASS khi chưa xác thực.
    /// </summary>
    public static async Task Test_SessionProbe_And_IProfileIdentityResolver_NoFakePass()
    {
        var (connection, factory) = await CreateInMemoryDbContextFactoryAsync();
        await using (connection)
        {
            var registry = new ProfileRegistry(factory);
            await registry.EnsureProfileAsync("museai_unlinked", BrowserProfileKind.MuseAI);

            IProfileIdentityResolver resolver = new SessionProbe(registry);
            ISessionProbe probe = (ISessionProbe)resolver;

            // 1. Khi chưa có adapter thật và session chưa thẩm định -> Bắt buộc IsSuccess = false, SessionStatus = Unverified
            var unverifiedResult = await resolver.ResolveProfileIdentityAsync("museai_unlinked", "MuseAI");
            Assert(!unverifiedResult.IsSuccess, "ResolveProfileIdentityAsync tuyệt đối không được fake PASS.");
            Assert(unverifiedResult.SessionStatus == BrowserSessionStatus.Unverified, "SessionStatus phải là Unverified.");
            Assert(!string.IsNullOrWhiteSpace(unverifiedResult.ErrorMessage), "Phải có thông báo lỗi giải thích rõ ràng.");

            var probeResult = await probe.CheckSessionAsync("museai_unlinked");
            Assert(probeResult.SessionStatus == BrowserSessionStatus.Unverified, "Probe sessionStatus phải là Unverified.");

            // 2. Với profile đã Connected và có AccountIdentity hợp lệ
            await registry.EnsureProfileAsync("museai_linked", BrowserProfileKind.MuseAI, accountIdentity: "channel_tiktok_01");
            await registry.UpdateProfileStateAsync("museai_linked", sessionStatus: BrowserSessionStatus.Connected);

            var verifiedResult = await resolver.ResolveProfileIdentityAsync("museai_linked", "MuseAI");
            Assert(verifiedResult.IsSuccess, "Profile đã Connected và có AccountIdentity phải thành công.");
            Assert(verifiedResult.AccountIdentity == "channel_tiktok_01", "AccountIdentity phải khớp.");
            Assert(verifiedResult.SessionStatus == BrowserSessionStatus.Connected, "SessionStatus phải là Connected.");

            // 3. Với profile không tồn tại trong hệ thống
            var notFoundResult = await resolver.ResolveProfileIdentityAsync("non_existent_profile", "MuseAI");
            Assert(!notFoundResult.IsSuccess, "Profile không tồn tại phải trả về false.");
        }
    }

    /// <summary>
    /// Test 9: CompleteLoginHandoff - authSuccess = true nhưng SessionProbe trả về Unverified ->
    /// Checkpoint tuyệt đối KHÔNG được resolve, profile về Stopped, Unverified (chống ảo tưởng xác thực).
    /// </summary>
    public static async Task Test_CompleteLoginHandoff_ProbeUnverified_CheckpointNotResolved()
    {
        var (connection, factory) = await CreateInMemoryDbContextFactoryAsync();
        await using (connection)
        {
            var registry = new ProfileRegistry(factory);
            string profileId = "shared_ai";
            await registry.EnsureProfileAsync(profileId, BrowserProfileKind.SharedAI);

            var pool = new TabPool();
            using var loginCoord = new LoginCoordinator(registry);
            var handoff = new BrowserHandoffCoordinator(registry, pool, loginCoord, factory);

            // Bắt đầu handoff hiển thị
            await handoff.PrepareHandoffToVisibleLoginAsync(profileId, "job-unverified-test", BrowserReasonCode.LoginRequired);

            // Xác nhận có checkpoint chưa resolved
            await using (var db = factory())
            {
                var cp = await db.BrowserHandoffCheckpoints.FirstOrDefaultAsync(c => c.ProfileId == profileId && c.ResolvedAtUtc == null);
                Assert(cp != null, "Checkpoint handoff phải tồn tại trong SQLite trước khi complete.");
            }

            // Gọi CompleteLoginHandoffAsync với authSuccess = true, nhưng session probe thực tế trả về Unverified
            await handoff.CompleteLoginHandoffAsync(profileId, authSuccess: true);

            // Kiểm tra: Checkpoint tuyệt đối KHÔNG được resolve
            await using (var db = factory())
            {
                var cp = await db.BrowserHandoffCheckpoints.FirstOrDefaultAsync(c => c.ProfileId == profileId);
                Assert(cp != null, "Checkpoint phải tồn tại.");
                Assert(cp?.ResolvedAtUtc == null, "Checkpoint tuyệt đối KHÔNG được đánh dấu resolved khi session unverified.");
                Assert(cp?.ReasonCode == "login_unverified", "ReasonCode của checkpoint phải được cập nhật là login_unverified.");
            }

            var updatedProfile = await registry.GetProfileAsync(profileId);
            Assert(updatedProfile?.VisibilityState == BrowserVisibilityState.Hidden, "Profile phải về Hidden.");
            Assert(updatedProfile?.OperationalState == BrowserOperationalState.Stopped, "Profile phải về Stopped (không được về Idle).");
            Assert(updatedProfile?.SessionStatus == BrowserSessionStatus.Unverified, "SessionStatus phải giữ Unverified.");

            // Xác nhận Semaphore hiển thị đã được giải phóng
            Assert(!loginCoord.IsVisibleLoginActive, "Slot hiển thị của loginCoordinator phải được giải phóng.");

            // Xác nhận cờ PauseDispatch đã được gỡ bỏ
            Assert(!handoff.IsDispatchPaused(profileId), "Cờ PauseDispatch phải được bỏ.");
        }
    }

    /// <summary>
    /// Test 10: PlaywrightBrowserController - Single-Instance Lock (.titoai_lock) Lifecycle,
    /// bảo vệ Chromium userDataDir, ngăn đồng thời 2 tiến trình, đóng an toàn và giải phóng lock.
    /// </summary>
    public static async Task Test_PlaywrightBrowserController_SingleInstanceLock_LifecycleAndSafety()
    {
        var (connection, factory) = await CreateInMemoryDbContextFactoryAsync();
        await using (connection)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "titoai_pw_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var registry = new ProfileRegistry(factory, tempDir);
                string profileId = "shared_ai";
                await registry.EnsureProfileAsync(profileId, BrowserProfileKind.SharedAI);

                using var owner = new ProfileOwner(pid => registry.GetUserDataDir(pid, BrowserProfileKind.SharedAI));

                var mockContextProxy = DispatchProxy.Create<IBrowserContext, MockBrowserContextProxy>();
                var mockContext = (MockBrowserContextProxy)(object)mockContextProxy;

                var controller = new PlaywrightBrowserController(
                    registry,
                    owner,
                    customPersistentLauncher: (userDataDir, options) =>
                    {
                        Assert(options.Headless == true, "Headless option phải đúng theo yêu cầu.");
                        Assert(options.Args != null && options.Args.Contains("--disable-blink-features=AutomationControlled"),
                            "Args phải chứa cờ --disable-blink-features=AutomationControlled.");
                        return Task.FromResult(mockContextProxy);
                    });

                await using (controller)
                {
                    // 1. Khởi chạy persistent context
                    var handle = await controller.LaunchPersistentContextAsync(profileId, isHeadless: true);
                    Assert(controller.IsRunning(profileId), "Controller phải báo IsRunning = true.");
                    Assert(owner.IsLocked(profileId), "Profile phải bị khóa bởi Single-Instance Lock.");

                    string lockFilePath = Path.Combine(registry.GetUserDataDir(profileId, BrowserProfileKind.SharedAI), ".titoai_lock");
                    Assert(File.Exists(lockFilePath), "Tệp .titoai_lock phải tồn tại trên đĩa.");

                    // Kiểm tra file lock cấp HĐH (không cho phép mở stream độc quyền khác)
                    bool lockExclusive = false;
                    try
                    {
                        using var stream = new FileStream(lockFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    }
                    catch (IOException)
                    {
                        lockExclusive = true;
                    }
                    Assert(lockExclusive, "Tệp .titoai_lock phải bị khóa cấp HĐH khi browser đang chạy.");

                    // Kiểm tra ngăn đồng thời: khởi chạy context thứ 2 trên cùng profileId phải ném lỗi
                    bool doubleLaunchBlocked = false;
                    try
                    {
                        await controller.LaunchPersistentContextAsync(profileId, isHeadless: true);
                    }
                    catch (InvalidOperationException)
                    {
                        doubleLaunchBlocked = true;
                    }
                    Assert(doubleLaunchBlocked, "Khởi chạy lần 2 trên cùng profile đang chạy phải bị chặn.");

                    // 2. Đóng context
                    await controller.CloseContextAsync(profileId);
                    Assert(!controller.IsRunning(profileId), "Controller phải báo IsRunning = false sau khi đóng.");
                    Assert(!owner.IsLocked(profileId), "Profile phải được mở khóa sau khi đóng.");
                    Assert(mockContext.WasClosed, "IBrowserContext.CloseAsync phải được gọi.");

                    var profile = await registry.GetProfileAsync(profileId);
                    Assert(profile?.OperationalState == BrowserOperationalState.Stopped, "Profile phải chuyển về Stopped.");
                    Assert(profile?.VisibilityState == BrowserVisibilityState.Hidden, "Profile phải chuyển về Hidden.");
                }
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, recursive: true); } catch { }
                }
            }
        }
    }

    /// <summary>
    /// Test 11: PlaywrightBrowserController - Xử lý lỗi driver Playwright chưa cài đặt (PlaywrightException),
    /// đảm bảo ném lỗi có cấu trúc, cập nhật Error state và GIẢI PHÓNG lock .titoai_lock an toàn (không rò rỉ).
    /// </summary>
    public static async Task Test_PlaywrightBrowserController_MissingDriverException_ReleasesLockSafely()
    {
        var (connection, factory) = await CreateInMemoryDbContextFactoryAsync();
        await using (connection)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "titoai_pw_driver_err_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var registry = new ProfileRegistry(factory, tempDir);
                string profileId = "shared_ai";
                await registry.EnsureProfileAsync(profileId, BrowserProfileKind.SharedAI);

                using var owner = new ProfileOwner(pid => registry.GetUserDataDir(pid, BrowserProfileKind.SharedAI));

                BrowserProfileStateChangedPayload? capturedPayload = null;
                registry.ProfileStateChanged += payload =>
                {
                    capturedPayload = payload;
                    return Task.CompletedTask;
                };

                // Launcher ném PlaywrightException giả lập máy chưa cài đặt Playwright Chromium driver
                var controller = new PlaywrightBrowserController(
                    registry,
                    owner,
                    customPersistentLauncher: (userDataDir, options) =>
                    {
                        throw new PlaywrightException("Executable doesn't exist at C:\\Users\\PC\\AppData\\Local\\ms-playwright\\chromium-1155\\chrome-win\\chrome.exe");
                    });

                await using (controller)
                {
                    bool exceptionCaught = false;
                    try
                    {
                        await controller.LaunchPersistentContextAsync(profileId, isHeadless: true);
                    }
                    catch (InvalidOperationException ex)
                    {
                        exceptionCaught = true;
                        Assert(ex.InnerException is PlaywrightException, "InnerException phải là PlaywrightException.");
                    }
                    Assert(exceptionCaught, "Phải ném InvalidOperationException có cấu trúc khi thiếu Playwright driver.");

                    // QUAN TRỌNG: Lock .titoai_lock phải được giải phóng an toàn, không bị treo/rò rỉ
                    Assert(!owner.IsLocked(profileId), "Lock single-instance phải được giải phóng sau lỗi PlaywrightException.");
                    Assert(!controller.IsRunning(profileId), "Controller không được ghi nhận tiến trình đang chạy.");

                    var profile = await registry.GetProfileAsync(profileId);
                    Assert(profile?.OperationalState == BrowserOperationalState.Error, "Profile phải chuyển về trạng thái Error.");
                    Assert(capturedPayload?.ReasonCode == "playwright_driver_error", "ReasonCode phát ra phải là playwright_driver_error.");
                }
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, recursive: true); } catch { }
                }
            }
        }
    }

    /// <summary>
    /// Mock Proxy tự động implement toàn bộ IBrowserContext thông qua BCL DispatchProxy.
    /// </summary>
    public class MockBrowserContextProxy : DispatchProxy
    {
        public bool WasClosed { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod == null) return null;

            if (targetMethod.Name == "CloseAsync")
            {
                WasClosed = true;
                return Task.CompletedTask;
            }

            if (targetMethod.ReturnType == typeof(Task))
            {
                return Task.CompletedTask;
            }

            if (targetMethod.ReturnType.IsGenericType &&
                targetMethod.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var resultType = targetMethod.ReturnType.GetGenericArguments()[0];
                var defaultVal = resultType.IsValueType ? Activator.CreateInstance(resultType) : null;
                var fromResultMethod = typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(resultType);
                return fromResultMethod.Invoke(null, [defaultVal]);
            }

            if (targetMethod.ReturnType.IsValueType)
            {
                return Activator.CreateInstance(targetMethod.ReturnType);
            }

            return null;
        }
    }

    /// <summary>
    /// Chạy toàn bộ các bài kiểm tra khói của T13.
    /// </summary>
    public static async Task RunAllAsync()
    {
        Console.WriteLine("\n[T13] Browser Runtime, Profile Registry & Login Handoff Smoke Checks:");

        async Task RunAsync(string name, Func<Task> test)
        {
            Console.Write($"  [RUN] {name} ... ");
            try
            {
                await test();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("PASS");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("FAIL");
                Console.ResetColor();
                Console.WriteLine($"        -> Lỗi: {ex.GetType().Name}: {ex.Message}");
                throw;
            }
        }

        await RunAsync("Test 1: Profile Registry & Path Safety (No email, traversal blocked)", Test_ProfileRegistry_PathSafety_NoEmailOrTraversal);
        await RunAsync("Test 2: Single-Instance Profile Lock (In-process & Cross-process/FileLock)", Test_SingleInstanceProfileLock_InProcessAndCrossProcess);
        await RunAsync("Test 3: Tab Pool Shared AI 3 Roles, Isolation & Quota Limits", Test_TabPool_SharedAi_QuotaAndRoleIsolation);
        await RunAsync("Test 4: Global One-Visible-Login Rule & VisibleWaiting Queue", Test_LoginCoordinator_OneVisibleLoginRule_AndQueue);
        await RunAsync("Test 5: User Closes Window -> auth_closed & No Auto-reopen", Test_LoginCoordinator_UserClosesWindow_AuthClosedReasonCode_NoAutoReopen);
        await RunAsync("Test 6: Quiescing & Drain Semantics for Shared AI", Test_QuiescingAndDrain_SharedAi_SafeRollbackPendingTabs);
        await RunAsync("Test 7: Crash Recovery at Startup (Reconcile Checkpoints & Dirty Profiles)", Test_CrashRecovery_ReconcileCheckpointsAtStartup);
        await RunAsync("Test 8: Session Probe & IProfileIdentityResolver Contracts (No fake PASS)", Test_SessionProbe_And_IProfileIdentityResolver_NoFakePass);
        await RunAsync("Test 9: CompleteLoginHandoff with Unverified SessionProbe does NOT resolve Checkpoint", Test_CompleteLoginHandoff_ProbeUnverified_CheckpointNotResolved);
        await RunAsync("Test 10: PlaywrightBrowserController Single-Instance Lock Lifecycle & Isolation", Test_PlaywrightBrowserController_SingleInstanceLock_LifecycleAndSafety);
        await RunAsync("Test 11: PlaywrightBrowserController Missing Driver Error Handling & Lock Release", Test_PlaywrightBrowserController_MissingDriverException_ReleasesLockSafely);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Assertion failed: {message}");
        }
    }
}
