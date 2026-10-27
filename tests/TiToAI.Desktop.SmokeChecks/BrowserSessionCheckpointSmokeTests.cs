using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using TiToAI.Browser;
using TiToAI.Browser.Abstractions;
using TiToAI.Browser.Models;
using TiToAI.Contracts.Browser;
using TiToAI.Contracts.Jobs;
using TiToAI.Desktop.Hosting;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Persistence.Entities;
using TiToAI.Infrastructure.Persistence.Interceptors;

namespace TiToAI.Desktop.SmokeChecks;

/// <summary>
/// Bộ kiểm thử khói định lượng Task T16: Browser & Session Checkpoint Coordinator.
/// Kiểm chứng 5 yêu cầu nghiệp vụ cốt lõi:
/// 1. Mất session trên profile A: các job của profile A chuyển AwaitingUser (session_expired);
///    các job của profile B vẫn chạy bình thường không bị ảnh hưởng (Profile Isolation).
/// 2. Quiesce an toàn: tab đang có side effect chưa hoàn tất không bị kill bạo lực.
/// 3. Login thành công & đúng identity: cửa sổ browser hiển thị được đóng về Hidden trước khi job được phép resume.
/// 4. Login phát hiện sai identity: đánh dấu wrong_account, chặn đứng resume.
/// 5. Đóng nút X khi login: chuyển auth_closed, không tự động retry/resend, bảo toàn session và có thể mở lại login.
/// </summary>
public static class BrowserSessionCheckpointSmokeTests
{
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"[ASSERTION FAILED] {message}");
        }
    }

    private static async Task<(SqliteConnection Connection, Func<TiToAIDbContext> Factory)> CreateInMemoryDbContextFactoryAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:;Default Timeout=5");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TiToAIDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;

        using (var initialDb = new TiToAIDbContext(options))
        {
            await initialDb.Database.EnsureCreatedAsync();
        }

        return (connection, () => new TiToAIDbContext(options));
    }

    private static async Task<(string WorkspaceId, string ProjectId)> EnsureWorkspaceAndProjectAsync(
        TiToAIDbContext db,
        string wsId = "ws1",
        string prjId = "proj1")
    {
        var nowIso = DateTime.UtcNow.ToString("o");
        if (!await db.Workspaces.AnyAsync(w => w.Id == wsId))
        {
            db.Workspaces.Add(new Workspace
            {
                Id = wsId,
                Name = "Workspace Smoke Test",
                ChannelHandle = "@smoke_channel",
                Topic = "AI Video Generation",
                Status = "Active",
                CreatedAtUtc = nowIso,
                UpdatedAtUtc = nowIso
            });
        }

        if (!await db.Projects.AnyAsync(p => p.Id == prjId))
        {
            db.Projects.Add(new Project
            {
                Id = prjId,
                WorkspaceId = wsId,
                Name = "Project Smoke Test",
                CreatedAtUtc = nowIso,
                UpdatedAtUtc = nowIso
            });
        }

        await db.SaveChangesAsync();
        return (wsId, prjId);
    }

    private static async Task EnsureProviderAccountAsync(
        TiToAIDbContext db,
        string accountId,
        string profileId,
        string providerKey = "chatgpt-web")
    {
        var nowIso = DateTime.UtcNow.ToString("o");
        if (!await db.ProviderAccounts.AnyAsync(a => a.Id == accountId))
        {
            db.ProviderAccounts.Add(new ProviderAccount
            {
                Id = accountId,
                ProviderKey = providerKey,
                ProfileId = profileId,
                DisplayName = $"Account {accountId}",
                VerifiedIdentity = $"{accountId}@example.com",
                IdentityVerifiedAtUtc = nowIso,
                Enabled = true,
                Lifecycle = "Active",
                MaxConcurrentJobs = 3,
                MaxTabs = 3,
                CreatedAtUtc = nowIso,
                UpdatedAtUtc = nowIso
            });
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Test 1: Mất session trên profile A:
    /// - Các job của profile A chuyển AwaitingUser (session_expired).
    /// - Các job của profile B vẫn chạy bình thường không bị ảnh hưởng (Profile Isolation).
    /// - Không được pause toàn app.
    /// </summary>
    public static async Task Test1_SessionLoss_IsolatesProfile_OtherProfilesUnaffectedAsync()
    {
        var (connection, factory) = await CreateInMemoryDbContextFactoryAsync();
        await using (connection)
        {
            var registry = new ProfileRegistry(factory);
            await registry.EnsureProfileAsync("profile_a", BrowserProfileKind.SharedAI);
            await registry.EnsureProfileAsync("profile_b", BrowserProfileKind.MuseAI);

            await registry.UpdateProfileStateAsync("profile_a", sessionStatus: BrowserSessionStatus.Connected, operationalState: BrowserOperationalState.Running);
            await registry.UpdateProfileStateAsync("profile_b", sessionStatus: BrowserSessionStatus.Connected, operationalState: BrowserOperationalState.Running);

            // Cài đặt dữ liệu DB: Workspace, Project, ProviderAccounts, Jobs và ExecutionBindings
            await using (var db = factory())
            {
                var (wsId, prjId) = await EnsureWorkspaceAndProjectAsync(db, "ws1", "proj1");
                await EnsureProviderAccountAsync(db, "acc_a", "profile_a", "chatgpt-web");
                await EnsureProviderAccountAsync(db, "acc_b", "profile_b", "gemini-web");

                var nowIso = DateTime.UtcNow.ToString("o");

                db.Jobs.AddRange(
                    new Job
                    {
                        Id = "job_a1",
                        WorkspaceId = wsId,
                        ProjectId = prjId,
                        TaskType = JobTaskType.Script,
                        State = JobState.Running,
                        ProviderKey = "chatgpt-web",
                        CreatedAtUtc = nowIso,
                        UpdatedAtUtc = nowIso
                    },
                    new Job
                    {
                        Id = "job_a2",
                        WorkspaceId = wsId,
                        ProjectId = prjId,
                        TaskType = JobTaskType.Script,
                        State = JobState.Reserved,
                        ProviderKey = "chatgpt-web",
                        CreatedAtUtc = nowIso,
                        UpdatedAtUtc = nowIso
                    },
                    new Job
                    {
                        Id = "job_b1",
                        WorkspaceId = wsId,
                        ProjectId = prjId,
                        TaskType = JobTaskType.Script,
                        State = JobState.Running,
                        ProviderKey = "gemini-web",
                        CreatedAtUtc = nowIso,
                        UpdatedAtUtc = nowIso
                    });

                db.ExecutionBindings.AddRange(
                    new ExecutionBinding
                    {
                        JobId = "job_a1",
                        ProfileId = "profile_a",
                        ProviderAccountId = "acc_a",
                        TabSlotId = "slot_1",
                        AssignedAtUtc = nowIso
                    },
                    new ExecutionBinding
                    {
                        JobId = "job_a2",
                        ProfileId = "profile_a",
                        ProviderAccountId = "acc_a",
                        TabSlotId = "slot_2",
                        AssignedAtUtc = nowIso
                    },
                    new ExecutionBinding
                    {
                        JobId = "job_b1",
                        ProfileId = "profile_b",
                        ProviderAccountId = "acc_b",
                        TabSlotId = "slot_3",
                        AssignedAtUtc = nowIso
                    });

                await db.SaveChangesAsync();
            }

            using var loginCoord = new LoginCoordinator(registry);
            var sessionProbe = new FakeSessionProbe();
            using var checkpointCoord = new BrowserSessionCheckpointCoordinator(
                registry,
                loginCoord,
                sessionProbe,
                dbContextFactory: factory);

            checkpointCoord.TrackJob("profile_a", "job_a1", sideEffectSubmitted: false, state: JobState.Running);
            checkpointCoord.TrackJob("profile_a", "job_a2", sideEffectSubmitted: false, state: JobState.Reserved);
            checkpointCoord.TrackJob("profile_b", "job_b1", sideEffectSubmitted: false, state: JobState.Running);

            // Bắt đầu mất session trên profile A
            await checkpointCoord.HandleSessionLossAsync("profile_a", BrowserReasonCode.SessionExpired);

            // 1. Kiểm tra Profile A
            var profileA = await registry.GetProfileAsync("profile_a");
            Assert(profileA != null, "Profile A phải tồn tại.");
            Assert(profileA!.SessionStatus == BrowserSessionStatus.SessionExpired, "Profile A SessionStatus phải là SessionExpired.");
            Assert(profileA.OperationalState == BrowserOperationalState.Stopped, "Profile A OperationalState phải là Stopped.");
            Assert(profileA.VisibilityState == BrowserVisibilityState.Hidden, "Profile A VisibilityState phải là Hidden.");

            // 2. Kiểm tra Jobs của Profile A
            await using (var db = factory())
            {
                var jobA1 = await db.Jobs.FindAsync("job_a1");
                var jobA2 = await db.Jobs.FindAsync("job_a2");
                Assert(jobA1!.State == JobState.AwaitingUser, "Job A1 phải chuyển sang AwaitingUser.");
                Assert(jobA1.ReasonCode == BrowserReasonCode.SessionExpired, "Job A1 reasonCode phải là session_expired.");
                Assert(jobA2!.State == JobState.AwaitingUser, "Job A2 phải chuyển sang AwaitingUser.");
                Assert(jobA2.ReasonCode == BrowserReasonCode.SessionExpired, "Job A2 reasonCode phải là session_expired.");

                // 3. KIỂM TRA PROFILE ISOLATION: Profile B và Job B1 HOÀN TOÀN KHÔNG BỊ ẢNH HƯỞNG
                var jobB1 = await db.Jobs.FindAsync("job_b1");
                Assert(jobB1!.State == JobState.Running, "Job B1 của Profile B phải giữ nguyên trạng thái Running.");
                Assert(jobB1.ReasonCode == null, "Job B1 không được bị gán reasonCode mất session.");
            }

            var profileB = await registry.GetProfileAsync("profile_b");
            Assert(profileB!.SessionStatus == BrowserSessionStatus.Connected, "Profile B SessionStatus phải tiếp tục Connected.");
            Assert(profileB.OperationalState == BrowserOperationalState.Running, "Profile B OperationalState phải tiếp tục Running.");

            // In-memory tracking check
            var trackedA1 = checkpointCoord.GetTrackedJob("profile_a", "job_a1");
            var trackedB1 = checkpointCoord.GetTrackedJob("profile_b", "job_b1");
            Assert(trackedA1!.State == JobState.AwaitingUser, "Tracked Job A1 phải là AwaitingUser.");
            Assert(trackedB1!.State == JobState.Running, "Tracked Job B1 phải tiếp tục là Running.");
        }
    }

    /// <summary>
    /// Test 2: Quiesce an toàn:
    /// - Không cắt ngang tác vụ đang có side-effect chưa rõ kết quả.
    /// - Nếu tab đang ở giữa pre-commit side effect hoặc đang chờ ack từ provider thì giữ ngữ cảnh, không đóng ép buộc.
    /// </summary>
    public static async Task Test2_SafeQuiesce_InFlightSideEffectPreserved_NoForcedKillAsync()
    {
        var (connection, factory) = await CreateInMemoryDbContextFactoryAsync();
        await using (connection)
        {
            var registry = new ProfileRegistry(factory);
            await registry.EnsureProfileAsync("shared_ai", BrowserProfileKind.SharedAI);
            var tabPool = new TabPool();
            using var loginCoord = new LoginCoordinator(registry);
            var sessionProbe = new FakeSessionProbe();

            using var checkpointCoord = new BrowserSessionCheckpointCoordinator(
                registry,
                loginCoord,
                sessionProbe,
                tabPool: tabPool,
                dbContextFactory: factory);

            // Tình huống A: Cấp phát tab trong TabPool, chưa phát sinh side effect
            var tabHandle = await tabPool.AcquireTabAsync("shared_ai", SharedAiTabRole.Script, "job_script_1");
            checkpointCoord.TrackJob("shared_ai", "job_script_1", sideEffectSubmitted: false);

            bool canQuiesceNoSideEffect = checkpointCoord.CanSafelyQuiesce("shared_ai", new[] { "job_script_1" });
            Assert(canQuiesceNoSideEffect, "Khi chưa có side effect in-flight, CanSafelyQuiesce phải trả về true.");

            // Tình huống B: Tab đã commit side-effect (ví dụ vừa bấm gửi prompt sinh ảnh/video, đang chờ ack từ provider)
            tabHandle.IsSideEffectSubmitted = true;
            checkpointCoord.SetJobSideEffectSubmitted("shared_ai", "job_script_1", true);

            bool canQuiesceWithSideEffect = checkpointCoord.CanSafelyQuiesce("shared_ai", new[] { "job_script_1" });
            Assert(!canQuiesceWithSideEffect, "Khi tab có side-effect chưa hoàn tất, CanSafelyQuiesce phải trả về false (CẤM kill bạo lực!).");

            // Tình huống C: Kiểm tra qua database SQLite
            await using (var db = factory())
            {
                var (wsId, prjId) = await EnsureWorkspaceAndProjectAsync(db, "ws1", "proj1");
                var nowIso = DateTime.UtcNow.ToString("o");
                db.Jobs.Add(new Job
                {
                    Id = "job_db_inflight",
                    WorkspaceId = wsId,
                    ProjectId = prjId,
                    TaskType = JobTaskType.Script,
                    State = JobState.Running,
                    SideEffectSubmitted = 1,
                    ResultReference = null, // Chưa có kết quả đối soát
                    CreatedAtUtc = nowIso,
                    UpdatedAtUtc = nowIso
                });
                await db.SaveChangesAsync();
            }

            bool canQuiesceDbInflight = checkpointCoord.CanSafelyQuiesce("shared_ai", new[] { "job_db_inflight" });
            Assert(!canQuiesceDbInflight, "Job trong DB có SideEffectSubmitted=1 và ResultReference rỗng phải chặn Quiesce.");

            // Tình huống D: Khi job đã nhận kết quả thành công và tab hoàn tất
            await using (var db = factory())
            {
                var job = await db.Jobs.FindAsync("job_db_inflight");
                job!.ResultReference = "https://ai.provider.com/artifacts/img_01.png";
                await db.SaveChangesAsync();
            }
            tabHandle.IsSideEffectSubmitted = false;
            checkpointCoord.SetJobSideEffectSubmitted("shared_ai", "job_script_1", false);

            bool canQuiesceResolved = checkpointCoord.CanSafelyQuiesce("shared_ai", new[] { "job_db_inflight" });
            Assert(canQuiesceResolved, "Khi side effect đã được xác nhận kết quả, CanSafelyQuiesce trả về true.");
        }
    }

    /// <summary>
    /// Test 3: Login thủ công thành công & đúng Identity:
    /// - Khi người dùng bấm Đăng nhập: mở browser hiển thị (isHeadless = false).
    /// - Trong lúc cửa sổ còn visible: TUYỆT ĐỐI KHÔNG được resume job hay chạy automation.
    /// - Sau khi xác minh thành công và đúng Identity (identityMatches == true):
    ///   + BẮT BUỘC ĐÓNG CỬA SỔ TRÌNH DUYỆT (hoặc chuyển về Hidden) TRƯỚC KHI cho phép resume job!
    ///   + Cập nhật Profile: VisibilityState = Hidden, OperationalState = Stopped, SessionStatus = Connected.
    ///   + Job chưa submit side-effect đủ điều kiện Resume chuyển về Queued.
    ///   + Job đã submit side-effect đủ điều kiện Resume chuyển về Reconcile.
    /// </summary>
    public static async Task Test3_LoginSuccessAndCorrectIdentity_WindowHiddenBeforeResumeAsync()
    {
        var (connection, factory) = await CreateInMemoryDbContextFactoryAsync();
        await using (connection)
        {
            var registry = new ProfileRegistry(factory);
            await registry.EnsureProfileAsync("profile_auth_ok", BrowserProfileKind.SharedAI, accountIdentity: "user@titoai.internal");
            await registry.UpdateProfileStateAsync("profile_auth_ok", sessionStatus: BrowserSessionStatus.SessionExpired, operationalState: BrowserOperationalState.Stopped);

            var fakeController = new FakeBrowserProcessController();
            using var loginCoord = new LoginCoordinator(registry, fakeController);
            var fakeProbe = new FakeSessionProbe
            {
                Status = BrowserSessionStatus.Connected,
                IdentityMatches = true
            };

            using var checkpointCoord = new BrowserSessionCheckpointCoordinator(
                registry,
                loginCoord,
                fakeProbe,
                processController: fakeController,
                dbContextFactory: factory);

            // Cài đặt 2 job ở trạng thái AwaitingUser
            checkpointCoord.TrackJob("profile_auth_ok", "job_pre_submit", sideEffectSubmitted: false, state: JobState.AwaitingUser);
            checkpointCoord.TrackJob("profile_auth_ok", "job_post_submit", sideEffectSubmitted: true, state: JobState.AwaitingUser);

            await using (var db = factory())
            {
                var (wsId, prjId) = await EnsureWorkspaceAndProjectAsync(db, "ws1", "proj1");
                await EnsureProviderAccountAsync(db, "acc_auth_ok", "profile_auth_ok", "chatgpt-web");

                var nowIso = DateTime.UtcNow.ToString("o");
                db.Jobs.AddRange(
                    new Job
                    {
                        Id = "job_pre_submit",
                        WorkspaceId = wsId,
                        ProjectId = prjId,
                        TaskType = "Script",
                        State = JobState.AwaitingUser,
                        ReasonCode = BrowserReasonCode.LoginRequired,
                        SideEffectSubmitted = 0,
                        CreatedAtUtc = nowIso,
                        UpdatedAtUtc = nowIso
                    },
                    new Job
                    {
                        Id = "job_post_submit",
                        WorkspaceId = wsId,
                        ProjectId = prjId,
                        TaskType = JobTaskType.Script,
                        State = JobState.AwaitingUser,
                        ReasonCode = BrowserReasonCode.LoginRequired,
                        SideEffectSubmitted = 1,
                        CreatedAtUtc = nowIso,
                        UpdatedAtUtc = nowIso
                    });

                db.ExecutionBindings.AddRange(
                    new ExecutionBinding { JobId = "job_pre_submit", ProfileId = "profile_auth_ok", ProviderAccountId = "acc_auth_ok", TabSlotId = "slot_1", AssignedAtUtc = nowIso },
                    new ExecutionBinding { JobId = "job_post_submit", ProfileId = "profile_auth_ok", ProviderAccountId = "acc_auth_ok", TabSlotId = "slot_2", AssignedAtUtc = nowIso });
                await db.SaveChangesAsync();
            }

            // Bước 1: Người dùng bấm Đăng nhập -> Mở cửa sổ hiển thị (isHeadless = false)
            var loginReq = await checkpointCoord.RequestLoginAsync("profile_auth_ok");
            Assert(!loginReq.Queued, "Login request phải chiếm slot trực tiếp.");
            Assert(loginReq.VisibilityState == BrowserVisibilityState.VisibleActive, "Cửa sổ phải ở trạng thái VisibleActive.");
            Assert(loginCoord.IsVisibleLoginActive, "Slot hiển thị đang bận.");

            // Bước 2: KIỂM CHỨNG BẮT BUỘC: Khi cửa sổ còn VisibleActive, CẤM resume job!
            bool canResumeWhileVisible = checkpointCoord.CanResumeJob("profile_auth_ok", "job_pre_submit", sideEffectSubmitted: false);
            Assert(!canResumeWhileVisible, "Khi cửa sổ đang hiển thị visible, CanResumeJob PHẢI trả về false.");
            Assert(checkpointCoord.GetResumeTargetState("profile_auth_ok", "job_pre_submit", sideEffectSubmitted: false) == "Blocked",
                "Khi cửa sổ hiển thị, GetResumeTargetState phải trả về Blocked.");

            // Bước 3: Xác minh login thành công và đúng Identity
            var verifyResult = await checkpointCoord.VerifyAndCompleteLoginAsync("profile_auth_ok", "user@titoai.internal");
            Assert(verifyResult.Success, "VerifyAndCompleteLoginAsync phải thành công.");
            Assert(verifyResult.IdentityMatches, "IdentityMatches phải là true.");

            // Bước 4: KIỂM CHỨNG BẮT BUỘC: Cửa sổ browser BẮT BUỘC đã được đóng về Hidden TRƯỚC KHI cho resume
            Assert(!loginCoord.IsVisibleLoginActive, "Cửa sổ visible phải được đóng hoàn toàn sau verify.");
            var profileAfterLogin = await registry.GetProfileAsync("profile_auth_ok");
            Assert(profileAfterLogin!.VisibilityState == BrowserVisibilityState.Hidden, "VisibilityState phải được đưa về Hidden.");
            Assert(profileAfterLogin.OperationalState == BrowserOperationalState.Stopped, "OperationalState phải là Stopped.");
            Assert(profileAfterLogin.SessionStatus == BrowserSessionStatus.Connected, "SessionStatus phải là Connected.");

            // Bước 5: Kiểm tra đủ điều kiện Resume sau khi đã về Hidden
            // Job chưa submit side-effect -> resume chuyển về Queued
            bool canResumePre = checkpointCoord.CanResumeJob("profile_auth_ok", "job_pre_submit", sideEffectSubmitted: false);
            Assert(canResumePre, "Job chưa submit phải đủ điều kiện resume.");
            string targetPre = checkpointCoord.GetResumeTargetState("profile_auth_ok", "job_pre_submit", sideEffectSubmitted: false);
            Assert(targetPre == JobState.Queued, "Job chưa submit phải chuyển về Queued.");

            string resumedPreState = await checkpointCoord.ResumeJobAsync("profile_auth_ok", "job_pre_submit", sideEffectSubmitted: false);
            Assert(resumedPreState == JobState.Queued, "ResumeJobAsync cho job chưa submit phải trả về Queued.");

            // Job đã submit side-effect -> resume chuyển về Reconcile (CẤM retry mù)
            bool canResumePost = checkpointCoord.CanResumeJob("profile_auth_ok", "job_post_submit", sideEffectSubmitted: true);
            Assert(canResumePost, "Job đã submit đủ điều kiện resume về Reconcile.");
            string targetPost = checkpointCoord.GetResumeTargetState("profile_auth_ok", "job_post_submit", sideEffectSubmitted: true);
            Assert(targetPost == "Reconcile", "Job đã submit phải chuyển về Reconcile để đối soát.");

            string resumedPostState = await checkpointCoord.ResumeJobAsync("profile_auth_ok", "job_post_submit", sideEffectSubmitted: true);
            Assert(resumedPostState == "Reconcile", "ResumeJobAsync cho job đã submit phải trả về Reconcile.");

            // Kiểm tra trạng thái DB
            await using (var db = factory())
            {
                var dbJobPre = await db.Jobs.FindAsync("job_pre_submit");
                var dbJobPost = await db.Jobs.FindAsync("job_post_submit");
                Assert(dbJobPre!.State == JobState.Queued, "DB Job pre-submit phải chuyển về Queued.");
                Assert(dbJobPre.ReasonCode == null, "DB Job pre-submit reasonCode phải được xóa sạch.");
                Assert(dbJobPost!.State == JobState.AwaitingUser, "DB Job post-submit tiếp tục giữ AwaitingUser để reconcile.");
                Assert(dbJobPost.ReasonCode == JobReasonCode.ReconcileRequired, "DB Job post-submit reasonCode phải là reconcile_required.");
            }
        }
    }

    /// <summary>
    /// Test 4: Login phát hiện sai identity (identityMatches == false):
    /// - Đánh dấu reason_code = 'wrong_account'.
    /// - TUYỆT ĐỐI KHÔNG được resume job! Báo sai tài khoản và yêu cầu đăng nhập đúng tài khoản đã liên kết.
    /// </summary>
    public static async Task Test4_LoginWrongIdentity_MarksWrongAccount_BlocksResumeAsync()
    {
        var (connection, factory) = await CreateInMemoryDbContextFactoryAsync();
        await using (connection)
        {
            var registry = new ProfileRegistry(factory);
            await registry.EnsureProfileAsync("profile_wrong_acc", BrowserProfileKind.SharedAI, accountIdentity: "expected_owner@domain.com");

            var fakeController = new FakeBrowserProcessController();
            using var loginCoord = new LoginCoordinator(registry, fakeController);

            // Giả lập probe trả về IdentityMatches = false (người dùng đăng nhập tài khoản khác trên web)
            var fakeProbe = new FakeSessionProbe
            {
                Status = BrowserSessionStatus.Connected,
                IdentityMatches = false
            };

            using var checkpointCoord = new BrowserSessionCheckpointCoordinator(
                registry,
                loginCoord,
                fakeProbe,
                processController: fakeController,
                dbContextFactory: factory);

            checkpointCoord.TrackJob("profile_wrong_acc", "job_w1", sideEffectSubmitted: false, state: JobState.AwaitingUser);

            await using (var db = factory())
            {
                var (wsId, prjId) = await EnsureWorkspaceAndProjectAsync(db, "ws1", "proj1");
                await EnsureProviderAccountAsync(db, "acc_w", "profile_wrong_acc", "chatgpt-web");

                var nowIso = DateTime.UtcNow.ToString("o");
                db.Jobs.Add(new Job
                {
                    Id = "job_w1",
                    WorkspaceId = wsId,
                    ProjectId = prjId,
                    TaskType = "Script",
                    State = JobState.AwaitingUser,
                    CreatedAtUtc = nowIso,
                    UpdatedAtUtc = nowIso
                });
                db.ExecutionBindings.Add(new ExecutionBinding
                {
                    JobId = "job_w1",
                    ProfileId = "profile_wrong_acc",
                    ProviderAccountId = "acc_w",
                    TabSlotId = "slot_w",
                    AssignedAtUtc = nowIso
                });
                await db.SaveChangesAsync();
            }

            // Mở cửa sổ đăng nhập
            await checkpointCoord.RequestLoginAsync("profile_wrong_acc");

            // Xác minh phát hiện sai danh tính
            var verifyResult = await checkpointCoord.VerifyAndCompleteLoginAsync("profile_wrong_acc", "expected_owner@domain.com");

            // Kiểm tra kết quả thẩm định
            Assert(!verifyResult.Success, "Verify phải trả về thất bại khi sai identity.");
            Assert(!verifyResult.IdentityMatches, "IdentityMatches phải là false.");
            Assert(verifyResult.ReasonCode == BrowserReasonCode.WrongAccount, "ReasonCode phải là wrong_account.");

            // Kiểm tra trạng thái profile
            var profile = await registry.GetProfileAsync("profile_wrong_acc");
            Assert(profile!.VisibilityState == BrowserVisibilityState.Hidden, "Cửa sổ phải đóng về Hidden.");
            Assert(profile.OperationalState == BrowserOperationalState.Stopped, "OperationalState phải là Stopped.");

            // KIỂM CHỨNG BẮT BUỘC: TUYỆT ĐỐI KHÔNG ĐƯỢC RESUME JOB
            bool canResume = checkpointCoord.CanResumeJob("profile_wrong_acc", "job_w1", sideEffectSubmitted: false);
            Assert(!canResume, "TUYỆT ĐỐI KHÔNG được phép resume job khi sai tài khoản!");
            Assert(checkpointCoord.GetResumeTargetState("profile_wrong_acc", "job_w1", sideEffectSubmitted: false) == "Blocked",
                "Trạng thái resume đích phải là Blocked.");

            string resumeAttempt = await checkpointCoord.ResumeJobAsync("profile_wrong_acc", "job_w1", sideEffectSubmitted: false);
            Assert(resumeAttempt == "Blocked", "ResumeJobAsync phải trả về Blocked.");

            // Kiểm tra job tiếp tục giữ AwaitingUser với reason_code = wrong_account
            await using (var db = factory())
            {
                var job = await db.Jobs.FindAsync("job_w1");
                Assert(job!.State == JobState.AwaitingUser, "Job phải tiếp tục giữ AwaitingUser.");
                Assert(job.ReasonCode == BrowserReasonCode.WrongAccount, "Job ReasonCode phải là wrong_account.");
            }
        }
    }

    /// <summary>
    /// Test 5: Người dùng đóng cửa sổ bằng nút X trước khi xác minh:
    /// - Nhận sự kiện context closed (tận dụng cơ chế BRW-1).
    /// - Cập nhật Profile sang Hidden/Stopped với reason_code = 'auth_closed'.
    /// - TUYỆT ĐỐI KHÔNG tự động retry / resend job!
    /// - Job tiếp tục giữ ở AwaitingUser với reason_code = 'auth_closed'.
    /// - Cookie/session hiện tại được bảo toàn (không bị logout ngoài ý muốn).
    /// - Người dùng có thể bấm nút Đăng nhập lại bất cứ lúc nào.
    /// </summary>
    public static async Task Test5_LoginClosedByXButton_AuthClosed_NoAutoRetry_PreservesSessionAsync()
    {
        var (connection, factory) = await CreateInMemoryDbContextFactoryAsync();
        await using (connection)
        {
            var registry = new ProfileRegistry(factory);
            await registry.EnsureProfileAsync("profile_x_close", BrowserProfileKind.SharedAI);
            // Giả lập phiên đang có sẵn cookie nhưng user mở lại login
            await registry.UpdateProfileStateAsync("profile_x_close", sessionStatus: BrowserSessionStatus.Connected);

            var fakeController = new FakeBrowserProcessController();
            using var loginCoord = new LoginCoordinator(registry, fakeController);
            var fakeProbe = new FakeSessionProbe();

            using var checkpointCoord = new BrowserSessionCheckpointCoordinator(
                registry,
                loginCoord,
                fakeProbe,
                processController: fakeController,
                dbContextFactory: factory);

            checkpointCoord.TrackJob("profile_x_close", "job_x1", sideEffectSubmitted: false, state: JobState.AwaitingUser);

            await using (var db = factory())
            {
                var (wsId, prjId) = await EnsureWorkspaceAndProjectAsync(db, "ws1", "proj1");
                await EnsureProviderAccountAsync(db, "acc_x", "profile_x_close", "chatgpt-web");

                var nowIso = DateTime.UtcNow.ToString("o");
                db.Jobs.Add(new Job
                {
                    Id = "job_x1",
                    WorkspaceId = wsId,
                    ProjectId = prjId,
                    TaskType = "Script",
                    State = JobState.AwaitingUser,
                    CreatedAtUtc = nowIso,
                    UpdatedAtUtc = nowIso
                });
                db.ExecutionBindings.Add(new ExecutionBinding
                {
                    JobId = "job_x1",
                    ProfileId = "profile_x_close",
                    ProviderAccountId = "acc_x",
                    TabSlotId = "slot_x",
                    AssignedAtUtc = nowIso
                });
                await db.SaveChangesAsync();
            }

            // Mở cửa sổ đăng nhập
            await checkpointCoord.RequestLoginAsync("profile_x_close");
            Assert(loginCoord.IsVisibleLoginActive, "Cửa sổ visible đang mở.");

            // Người dùng đóng cửa sổ bằng nút X -> kích hoạt sự kiện ContextClosed
            fakeController.TriggerContextClosed("profile_x_close");
            // Đợi bộ điều phối xử lý xong sự kiện đóng cửa sổ
            await Task.Delay(150);

            // 1. Kiểm tra giải phóng slot hiển thị
            Assert(!loginCoord.IsVisibleLoginActive, "Slot hiển thị phải được giải phóng.");

            // 2. Profile chuyển Hidden/Stopped kèm reason_code = 'auth_closed'
            var profile = await registry.GetProfileAsync("profile_x_close");
            Assert(profile!.VisibilityState == BrowserVisibilityState.Hidden, "VisibilityState phải là Hidden.");
            Assert(profile.OperationalState == BrowserOperationalState.Stopped, "OperationalState phải là Stopped.");

            // 3. BẢO TOÀN SESSION COOKIES: không bị xóa hay ép về SessionExpired
            Assert(profile.SessionStatus == BrowserSessionStatus.Connected, "SessionStatus hiện tại phải được bảo toàn.");

            // 4. TUYỆT ĐỐI KHÔNG TỰ ĐỘNG RETRY / RESEND JOB
            bool canResume = checkpointCoord.CanResumeJob("profile_x_close", "job_x1", sideEffectSubmitted: false);
            Assert(!canResume, "TUYỆT ĐỐI KHÔNG tự động retry / resend job khi user bấm X!");
            Assert(checkpointCoord.GetResumeTargetState("profile_x_close", "job_x1", sideEffectSubmitted: false) == "Blocked",
                "Trạng thái resume đích phải là Blocked.");

            await using (var db = factory())
            {
                var job = await db.Jobs.FindAsync("job_x1");
                Assert(job!.State == JobState.AwaitingUser, "Job tiếp tục giữ ở AwaitingUser.");
                Assert(job.ReasonCode == BrowserReasonCode.AuthClosed, "Job ReasonCode phải là auth_closed.");
            }

            // 5. NGƯỜI DÙNG CÓ THỂ BẤM NÚT ĐĂNG NHẬP LẠI BẤT CỨ LÚC NÀO
            var reopenLogin = await checkpointCoord.RequestLoginAsync("profile_x_close");
            Assert(!reopenLogin.Queued, "Có thể mở lại cửa sổ đăng nhập thành công.");
            Assert(reopenLogin.VisibilityState == BrowserVisibilityState.VisibleActive, "Cửa sổ mở lại ở trạng thái VisibleActive.");
            Assert(loginCoord.IsVisibleLoginActive, "Slot hiển thị được cấp lại cho profile.");
        }
    }

    /// <summary>
    /// Test 6: Kiểm chứng tích hợp với production host / gatekeeper / service:
    /// - Khởi tạo BrowserSessionCheckpointCoordinator với ProfileRegistry, LoginCoordinator, SessionProbe, PlaywrightBrowserController, TabPool, và dbContextFactory.
    /// - Kết nối trực tiếp với HostBrowserGatekeeper.
    /// - Kịch bản hoàn chỉnh:
    ///   1. Profile A mất session: chỉ ảnh hưởng Profile A và các job của Profile A (Profile Isolation, Profile B không bị ảnh hưởng).
    ///   2. Job trên Profile A chuyển AwaitingUser (Job có side effect in-flight chuyển AwaitingUser với reconcile_required và giữ occupancy).
    ///   3. Mở login: cố resume khi cửa sổ còn mở (VisibleActive) bị từ chối cả ở Gatekeeper và Coordinator.
    ///   4. Probe sai identity: phát hiện wrong_account, đóng về Hidden, chặn đứng resume.
    ///   5. Mở lại login, probe đúng identity và đóng về Hidden: resume thành công (job chưa submit về Queued, job đã submit về Reconcile)!
    /// </summary>
    public static async Task Test6_HostGatekeeperIntegration_Isolation_IdentityProbe_ResumeAsync()
    {
        var (connection, factory) = await CreateInMemoryDbContextFactoryAsync();
        await using (connection)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "titoai_checkpoint_t6_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var registry = new ProfileRegistry(factory, tempDir);
                string profileA = "profile_prod_a";
                string profileB = "profile_prod_b";

                await registry.EnsureProfileAsync(profileA, BrowserProfileKind.SharedAI, accountIdentity: "user_a@domain.com");
                await registry.EnsureProfileAsync(profileB, BrowserProfileKind.MuseAI, accountIdentity: "user_b@domain.com");

                await registry.UpdateProfileStateAsync(
                    profileA,
                    sessionStatus: BrowserSessionStatus.Connected,
                    operationalState: BrowserOperationalState.Running,
                    visibilityState: BrowserVisibilityState.Hidden);

                await registry.UpdateProfileStateAsync(
                    profileB,
                    sessionStatus: BrowserSessionStatus.Connected,
                    operationalState: BrowserOperationalState.Running,
                    visibilityState: BrowserVisibilityState.Hidden);

                using var owner = new ProfileOwner(pid => registry.GetUserDataDir(pid, BrowserProfileKind.SharedAI));

                // Khởi tạo mock Playwright browser context và page thông qua BrowserSmokeTests proxy
                var mockContextProxy = System.Reflection.DispatchProxy.Create<IBrowserContext, BrowserSmokeTests.MockBrowserContextProxy>();
                var mockContext = (BrowserSmokeTests.MockBrowserContextProxy)(object)mockContextProxy;
                var mockPageProxy = System.Reflection.DispatchProxy.Create<IPage, BrowserSmokeTests.MockPageProxy>();
                mockContext.AddPage(mockPageProxy);

                var controller = new PlaywrightBrowserController(
                    registry,
                    owner,
                    customPersistentLauncher: (dir, opts) => Task.FromResult(mockContextProxy));

                var tabPool = new TabPool();
                using var loginCoord = new LoginCoordinator(registry, controller);

                // Khởi tạo ISessionProbe thật với adapter điều khiển kết quả động
                string probedStatus = BrowserSessionStatus.Connected;
                bool? probedIdentityMatches = true;

                var sessionProbe = new SessionProbe(
                    registry,
                    sessionCheckAdapter: (pid, ct) => Task.FromResult<BrowserCheckSessionResponseData?>(
                        new BrowserCheckSessionResponseData(probedStatus, probedIdentityMatches)));

                using var checkpointCoord = new BrowserSessionCheckpointCoordinator(
                    registry,
                    loginCoord,
                    sessionProbe,
                    processController: controller,
                    tabPool: tabPool,
                    dbContextFactory: factory);

                using var gatekeeper = new HostBrowserGatekeeper(
                    registry,
                    loginCoord,
                    tabPool,
                    controller,
                    checkpointCoord);

                // Cài đặt dữ liệu DB
                await using (var db = factory())
                {
                    var (wsId, prjId) = await EnsureWorkspaceAndProjectAsync(db, "ws_prod", "proj_prod");
                    await EnsureProviderAccountAsync(db, "acc_a", profileA, "chatgpt-web");
                    await EnsureProviderAccountAsync(db, "acc_b", profileB, "gemini-web");

                    var nowIso = DateTime.UtcNow.ToString("o");
                    db.Jobs.AddRange(
                        new Job
                        {
                            Id = "job_prod_a1",
                            WorkspaceId = wsId,
                            ProjectId = prjId,
                            TaskType = JobTaskType.Script,
                            State = JobState.Running,
                            ProviderKey = "chatgpt-web",
                            SideEffectSubmitted = 0,
                            CreatedAtUtc = nowIso,
                            UpdatedAtUtc = nowIso
                        },
                        new Job
                        {
                            Id = "job_prod_a2",
                            WorkspaceId = wsId,
                            ProjectId = prjId,
                            TaskType = JobTaskType.Script,
                            State = JobState.Running,
                            ProviderKey = "chatgpt-web",
                            SideEffectSubmitted = 1,
                            CreatedAtUtc = nowIso,
                            UpdatedAtUtc = nowIso
                        },
                        new Job
                        {
                            Id = "job_prod_b1",
                            WorkspaceId = wsId,
                            ProjectId = prjId,
                            TaskType = JobTaskType.Script,
                            State = JobState.Running,
                            ProviderKey = "gemini-web",
                            SideEffectSubmitted = 0,
                            CreatedAtUtc = nowIso,
                            UpdatedAtUtc = nowIso
                        });

                    db.ExecutionBindings.AddRange(
                        new ExecutionBinding { JobId = "job_prod_a1", ProfileId = profileA, ProviderAccountId = "acc_a", TabSlotId = "slot_a1", AssignedAtUtc = nowIso },
                        new ExecutionBinding { JobId = "job_prod_a2", ProfileId = profileA, ProviderAccountId = "acc_a", TabSlotId = "slot_a2", AssignedAtUtc = nowIso },
                        new ExecutionBinding { JobId = "job_prod_b1", ProfileId = profileB, ProviderAccountId = "acc_b", TabSlotId = "slot_b1", AssignedAtUtc = nowIso });

                    await db.SaveChangesAsync();
                }

                // Đăng ký theo dõi qua Gatekeeper
                gatekeeper.TrackJob(profileA, "job_prod_a1", sideEffectSubmitted: false, state: JobState.Running);
                gatekeeper.TrackJob(profileA, "job_prod_a2", sideEffectSubmitted: true, state: JobState.Running);
                gatekeeper.TrackJob(profileB, "job_prod_b1", sideEffectSubmitted: false, state: JobState.Running);

                // =========================================================================
                // BƯỚC 1: Profile A mất session -> Chỉ ảnh hưởng Profile A (Profile Isolation)
                // =========================================================================
                await checkpointCoord.HandleSessionLossAsync(profileA, BrowserReasonCode.SessionExpired);

                var profA = await registry.GetProfileAsync(profileA);
                Assert(profA!.SessionStatus == BrowserSessionStatus.SessionExpired, "Profile A phải có SessionStatus = SessionExpired.");
                Assert(profA.OperationalState == BrowserOperationalState.Stopped, "Profile A phải có OperationalState = Stopped.");
                Assert(profA.VisibilityState == BrowserVisibilityState.Hidden, "Profile A phải có VisibilityState = Hidden.");

                await using (var db = factory())
                {
                    var dbJobA1 = await db.Jobs.FindAsync("job_prod_a1");
                    var dbJobA2 = await db.Jobs.FindAsync("job_prod_a2");
                    Assert(dbJobA1!.State == JobState.AwaitingUser, "Job A1 phải chuyển sang AwaitingUser.");
                    Assert(dbJobA1.ReasonCode == BrowserReasonCode.SessionExpired, "Job A1 chưa submit phải có reason = session_expired.");
                    Assert(dbJobA2!.State == JobState.AwaitingUser, "Job A2 in-flight phải chuyển sang AwaitingUser.");
                    Assert(dbJobA2.ReasonCode == JobReasonCode.ReconcileRequired, "Job A2 in-flight phải có reason = reconcile_required.");

                    // Giữ occupancy: ExecutionBinding của Job A2 tuyệt đối không bị xóa!
                    var bindingA2 = await db.ExecutionBindings.FirstOrDefaultAsync(b => b.JobId == "job_prod_a2");
                    Assert(bindingA2 != null, "ExecutionBinding của Job A2 phải được giữ nguyên để bảo tồn occupancy.");

                    // PROFILE ISOLATION: Profile B và Job B1 hoàn toàn không bị ảnh hưởng!
                    var dbJobB1 = await db.Jobs.FindAsync("job_prod_b1");
                    Assert(dbJobB1!.State == JobState.Running, "Job B1 của Profile B phải giữ nguyên Running.");
                    Assert(dbJobB1.ReasonCode == null, "Job B1 không được bị gán reason mất session.");
                }

                var profB = await registry.GetProfileAsync(profileB);
                Assert(profB!.SessionStatus == BrowserSessionStatus.Connected, "Profile B phải tiếp tục giữ SessionStatus = Connected.");
                Assert(profB.OperationalState == BrowserOperationalState.Running, "Profile B phải tiếp tục giữ OperationalState = Running.");

                // =========================================================================
                // BƯỚC 2: Người dùng bấm Đăng nhập -> Cửa sổ mở (VisibleActive) -> CHẶN RESUME
                // =========================================================================
                var reqLogin = await checkpointCoord.RequestLoginAsync(profileA);
                Assert(!reqLogin.Queued, "Request login phải cấp slot trực tiếp.");
                Assert(reqLogin.VisibilityState == BrowserVisibilityState.VisibleActive, "Cửa sổ login phải ở trạng thái VisibleActive.");
                Assert(loginCoord.IsVisibleLoginActive, "LoginCoordinator phải ghi nhận cửa sổ visible đang mở.");
                Assert(gatekeeper.IsProfileBusyWithLogin(profileA), "Gatekeeper phải báo profile đang bận với login.");

                // Thử resume qua Gatekeeper và Coordinator trong lúc cửa sổ hiển thị
                bool canResumeWhileOpen = gatekeeper.CanResumeJob(profileA, "job_prod_a1", sideEffectSubmitted: false);
                Assert(!canResumeWhileOpen, "Gatekeeper.CanResumeJob PHẢI từ chối khi cửa sổ browser còn đang mở VisibleActive.");

                bool canResumeAsyncWhileOpen = await checkpointCoord.CanResumeJobAsync(profileA, "job_prod_a1", sideEffectSubmitted: false);
                Assert(!canResumeAsyncWhileOpen, "CanResumeJobAsync PHẢI trả về false khi cửa sổ browser đang mở.");

                string resumeAttemptWhileOpen = await checkpointCoord.ResumeJobAsync(profileA, "job_prod_a1", sideEffectSubmitted: false);
                Assert(resumeAttemptWhileOpen == "Blocked", "ResumeJobAsync khi cửa sổ mở phải trả về Blocked.");

                // =========================================================================
                // BƯỚC 3: Probe phát hiện sai identity -> wrong_account -> CHẶN ĐỨNG RESUME
                // =========================================================================
                probedStatus = BrowserSessionStatus.Connected;
                probedIdentityMatches = false; // Probe trả về sai identity

                var verifyWrong = await checkpointCoord.VerifyAndCompleteLoginAsync(profileA, "user_a@domain.com");
                Assert(!verifyWrong.Success, "Verify login phải trả về thất bại khi sai identity.");
                Assert(!verifyWrong.IdentityMatches, "IdentityMatches phải là false.");
                Assert(verifyWrong.ReasonCode == BrowserReasonCode.WrongAccount, "ReasonCode phải là wrong_account.");

                bool canResumeWrong = gatekeeper.CanResumeJob(profileA, "job_prod_a1", sideEffectSubmitted: false);
                Assert(!canResumeWrong, "Gatekeeper.CanResumeJob PHẢI từ chối khi phát hiện sai identity (wrong_account).");

                bool canResumeAsyncWrong = await checkpointCoord.CanResumeJobAsync(profileA, "job_prod_a1", sideEffectSubmitted: false);
                Assert(!canResumeAsyncWrong, "CanResumeJobAsync PHẢI trả về false khi sai identity (wrong_account).");

                string resumeAttemptWrong = await checkpointCoord.ResumeJobAsync(profileA, "job_prod_a1", sideEffectSubmitted: false);
                Assert(resumeAttemptWrong == "Blocked", "ResumeJobAsync khi wrong_account phải trả về Blocked.");

                // =========================================================================
                // BƯỚC 4: Mở lại login, probe đúng identity và đóng về Hidden -> RESUME THÀNH CÔNG!
                // =========================================================================
                await checkpointCoord.RequestLoginAsync(profileA);
                probedStatus = BrowserSessionStatus.Connected;
                probedIdentityMatches = true; // Probe xác nhận danh tính khớp chuẩn

                var verifyOk = await checkpointCoord.VerifyAndCompleteLoginAsync(profileA, "user_a@domain.com");
                Assert(verifyOk.Success, "Verify login với đúng danh tính phải thành công.");
                Assert(verifyOk.IdentityMatches, "IdentityMatches phải là true.");

                // Cửa sổ BẮT BUỘC đã được đóng về Hidden
                Assert(!loginCoord.IsVisibleLoginActive, "Cửa sổ visible phải được đóng sau khi xác minh xong.");
                var profAAfterVerify = await registry.GetProfileAsync(profileA);
                Assert(profAAfterVerify!.VisibilityState == BrowserVisibilityState.Hidden, "Profile A VisibilityState phải là Hidden.");
                Assert(profAAfterVerify.OperationalState == BrowserOperationalState.Stopped, "Profile A OperationalState phải là Stopped.");
                Assert(profAAfterVerify.SessionStatus == BrowserSessionStatus.Connected, "Profile A SessionStatus phải là Connected.");

                // Kiểm tra điều kiện resume
                bool canResumeOk = gatekeeper.CanResumeJob(profileA, "job_prod_a1", sideEffectSubmitted: false);
                Assert(canResumeOk, "Gatekeeper.CanResumeJob PHẢI trả về true khi đã về Hidden và đúng danh tính.");

                bool canResumeAsyncOk = await checkpointCoord.CanResumeJobAsync(profileA, "job_prod_a1", sideEffectSubmitted: false);
                Assert(canResumeAsyncOk, "CanResumeJobAsync PHẢI trả về true.");

                // Thực hiện Resume Job A1 (chưa submit side effect) -> chuyển về Queued
                string stateA1 = await checkpointCoord.ResumeJobAsync(profileA, "job_prod_a1", sideEffectSubmitted: false);
                Assert(stateA1 == JobState.Queued, "Job A1 chưa submit phải được resume về Queued.");

                // Thực hiện Resume Job A2 (đã submit side effect in-flight) -> chuyển về Reconcile để đối soát, CẤM retry mù
                string stateA2 = await checkpointCoord.ResumeJobAsync(profileA, "job_prod_a2", sideEffectSubmitted: true);
                Assert(stateA2 == "Reconcile", "Job A2 đã submit side effect phải được resume về Reconcile.");

                // Kiểm tra database
                await using (var db = factory())
                {
                    var dbA1 = await db.Jobs.FindAsync("job_prod_a1");
                    var dbA2 = await db.Jobs.FindAsync("job_prod_a2");
                    Assert(dbA1!.State == JobState.Queued, "DB Job A1 phải là Queued.");
                    Assert(dbA1.ReasonCode == null, "DB Job A1 reasonCode phải được xóa.");
                    Assert(dbA2!.State == JobState.AwaitingUser, "DB Job A2 phải giữ AwaitingUser.");
                    Assert(dbA2.ReasonCode == JobReasonCode.ReconcileRequired, "DB Job A2 reasonCode phải là reconcile_required.");

                    // Giữ occupancy
                    var bindingA2 = await db.ExecutionBindings.FirstOrDefaultAsync(b => b.JobId == "job_prod_a2");
                    Assert(bindingA2 != null, "ExecutionBinding của Job A2 tiếp tục giữ occupancy.");
                }

                // Dọn dẹp tracking qua Gatekeeper
                gatekeeper.UntrackJob(profileA, "job_prod_a1");
                gatekeeper.UntrackJob(profileA, "job_prod_a2");
                gatekeeper.UntrackJob(profileB, "job_prod_b1");
                Assert(checkpointCoord.GetTrackedJob(profileA, "job_prod_a1") == null, "UntrackJob phải xóa khỏi in-memory tracking.");
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
    /// Điểm chạy toàn bộ 6 test case của BrowserSessionCheckpointSmokeTests.
    /// </summary>
    public static async Task RunAllAsync()
    {
        Console.WriteLine("=================================================================");
        Console.WriteLine("  TiToAI — Browser Session Checkpoint Smoke Checks (Task T16)   ");
        Console.WriteLine("=================================================================");

        int passed = 0;
        int failed = 0;

        async Task RunCheckAsync(string name, Func<Task> testAction)
        {
            Console.Write($"[RUN] {name} ... ");
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
                    Console.WriteLine($"      -> Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                }
                Console.WriteLine($"      -> Trace: {ex.StackTrace}");
            }
        }

        await RunCheckAsync(
            "Test 1: Mất session Profile A chỉ ảnh hưởng Profile A, Profile B không bị ảnh hưởng (Profile Isolation)",
            Test1_SessionLoss_IsolatesProfile_OtherProfilesUnaffectedAsync);

        await RunCheckAsync(
            "Test 2: Quiesce an toàn: tác vụ đang có side-effect in-flight không bị kill bạo lực",
            Test2_SafeQuiesce_InFlightSideEffectPreserved_NoForcedKillAsync);

        await RunCheckAsync(
            "Test 3: Login thành công & đúng identity: cửa sổ đóng về Hidden trước khi job được phép resume",
            Test3_LoginSuccessAndCorrectIdentity_WindowHiddenBeforeResumeAsync);

        await RunCheckAsync(
            "Test 4: Login phát hiện sai identity: đánh dấu wrong_account và chặn đứng resume",
            Test4_LoginWrongIdentity_MarksWrongAccount_BlocksResumeAsync);

        await RunCheckAsync(
            "Test 5: Đóng nút X khi login: chuyển auth_closed, không tự động retry/resend, bảo toàn session",
            Test5_LoginClosedByXButton_AuthClosed_NoAutoRetry_PreservesSessionAsync);

        await RunCheckAsync(
            "Test 6: Tích hợp với Production Host / Gatekeeper / Service: Profile Isolation, In-flight Side-effect, Identity Verification & Resume",
            Test6_HostGatekeeperIntegration_Isolation_IdentityProbe_ResumeAsync);

        Console.WriteLine("-----------------------------------------------------------------");
        Console.WriteLine($"Kết quả Browser Session Checkpoint Tests: {passed} PASSED, {failed} FAILED.");
        Console.WriteLine("=================================================================\n");

        if (failed > 0)
        {
            throw new InvalidOperationException($"Có {failed} test case Browser Session Checkpoint thất bại.");
        }
    }

    #region Fake Test Doubles

    private sealed class FakeSessionProbe : ISessionProbe
    {
        public string Status { get; set; } = BrowserSessionStatus.Connected;
        public bool? IdentityMatches { get; set; } = true;

        public Task<BrowserCheckSessionResponseData> CheckSessionAsync(string profileId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new BrowserCheckSessionResponseData(Status, IdentityMatches));
        }
    }

    private sealed class FakeBrowserProcessController : IBrowserProcessController
    {
        private readonly HashSet<string> _runningProfiles = [];

        public event EventHandler<string>? ContextClosed;

        public Task<IBrowserContextHandle> LaunchPersistentContextAsync(string profileId, bool isHeadless, CancellationToken cancellationToken = default)
        {
            _runningProfiles.Add(profileId);
            return Task.FromResult<IBrowserContextHandle>(new FakeBrowserContextHandle(profileId, isHeadless));
        }

        public Task<IBrowserContextHandle> LaunchPersistentContextAsync(string profileId, bool isHeadless, string? initialUrl, CancellationToken cancellationToken = default)
        {
            _runningProfiles.Add(profileId);
            return Task.FromResult<IBrowserContextHandle>(new FakeBrowserContextHandle(profileId, isHeadless));
        }

        public Task NavigateAsync(string profileId, string url, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task CloseContextAsync(string profileId, CancellationToken cancellationToken = default)
        {
            _runningProfiles.Remove(profileId);
            ContextClosed?.Invoke(this, profileId);
            return Task.CompletedTask;
        }

        public bool IsRunning(string profileId) => _runningProfiles.Contains(profileId);

        public IBrowserContextHandle? TryGetContext(string profileId) => null;

        public Task PrewarmAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void TriggerContextClosed(string profileId)
        {
            _runningProfiles.Remove(profileId);
            ContextClosed?.Invoke(this, profileId);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }

    private sealed class FakeBrowserContextHandle : IBrowserContextHandle
    {
        public string ProfileId { get; }
        public bool IsHeadless { get; }
        public Microsoft.Playwright.IBrowserContext Context => null!;
        public IProfileOwnerLock OwnerLock => null!;

        public FakeBrowserContextHandle(string profileId, bool isHeadless)
        {
            ProfileId = profileId;
            IsHeadless = isHeadless;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }

    #endregion
}
