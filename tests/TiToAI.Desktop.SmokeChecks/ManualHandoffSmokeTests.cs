using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TiToAI.Application.Jobs;
using TiToAI.Application.Providers;
using TiToAI.Contracts.Jobs;
using TiToAI.Contracts.Providers;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Persistence.Entities;
using TiToAI.Infrastructure.Persistence.Interceptors;

namespace TiToAI.Desktop.SmokeChecks;

/// <summary>
/// Bộ kiểm thử khói SQLite kỹ thuật độc lập cho Manual Handoff (Contract v1.1 §4, §5 & Task T18c):
/// 1. GetHandoff trích xuất đúng prompt snapshot và schema cho từng loại task.
/// 2. Import Script JSON hợp lệ thành công, chuyển Succeeded và giải phóng slot occupancy.
/// 3. Import Script sai format (thiếu scenes, duration âm, order không liên tục...) bị từ chối với JobValidationException.
/// 4. Import VideoPrompt text hợp lệ thành công.
/// 5. Idempotency với cùng ClientRequestId trả kết quả cũ không tạo duplicate.
/// 6. Import kết quả sau khi Job đã Cancel giữ trạng thái Canceled, unlink asset, không ghi đè thành Succeeded.
/// 7. Concurrency conflict (sai expectedRevision) bị chặn bằng JobConcurrencyConflictException.
/// </summary>
public static class ManualHandoffSmokeTests
{
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"ASSERTION FAILED: {message}");
        }
    }

    private static (DbContextOptions<TiToAIDbContext> Options, string TempDir) CreateTestContext()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"titoai_handoff_smoke_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var dbPath = Path.Combine(tempDir, "titoai_handoff_test.db");
        var connectionString = DatabasePathHelper.BuildConnectionString(dbPath);

        var optionsBuilder = new DbContextOptionsBuilder<TiToAIDbContext>();
        optionsBuilder.UseSqlite(connectionString)
                      .AddInterceptors(new SqlitePragmaInterceptor())
                      .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        var dbOptions = optionsBuilder.Options;

        var now = DateTimeOffset.UtcNow.ToString("o");

        using (var db = new TiToAIDbContext(dbOptions))
        {
            db.Database.Migrate();

            // Seed Workspace
            var ws = new Workspace
            {
                Id = "ws_handoff",
                Name = "Handoff Smoke Workspace",
                Status = "Active",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Workspaces.Add(ws);

            // Seed Project
            var prj = new Project
            {
                Id = "prj_handoff",
                WorkspaceId = "ws_handoff",
                Name = "Handoff Test Project",
                Status = "Active",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Projects.Add(prj);

            // Seed Scene
            var scene = new Scene
            {
                Id = "scene_handoff_01",
                ProjectId = "prj_handoff",
                WorkspaceId = "ws_handoff",
                SequenceOrder = 1,
                Name = "Phân cảnh mở đầu",
                Status = "Draft",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Scenes.Add(scene);

            // Seed Provider Profile & Account
            var profile = new BrowserProfile
            {
                Id = "prof_handoff",
                Kind = "SharedAI",
                ProviderKey = "chatgpt-web",
                DisplayName = "Handoff Test Profile",
                SessionStatus = "Connected",
                OperationalState = "Stopped",
                VisibilityState = "Hidden",
                Lifecycle = "Active",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.BrowserProfiles.Add(profile);

            var acc = new ProviderAccount
            {
                Id = "acc_handoff",
                ProviderKey = "chatgpt-web",
                ProfileId = "prof_handoff",
                DisplayName = "Handoff Test Account",
                VerifiedIdentity = "test@handoff.ai",
                IdentityVerifiedAtUtc = now,
                Enabled = true,
                Lifecycle = "Active",
                MaxConcurrentJobs = 3,
                MaxTabs = 3,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.ProviderAccounts.Add(acc);

            db.SaveChanges();
        }

        return (dbOptions, tempDir);
    }

    public static async Task<int> RunAllAsync()
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine(" BỘ KIỂM THỬ KHÓI MANUAL HANDOFF CONTRACT / FLOW & TESTS (Task T18c & v1.1)");
        Console.WriteLine("================================================================================");

        var (dbOptions, tempDir) = CreateTestContext();
        var service = new ManualHandoffService(dbOptions);

        try
        {
            // -------------------------------------------------------------------------
            // TEST 1: GetHandoff trích xuất đúng prompt snapshot và schema cho từng loại task
            // -------------------------------------------------------------------------
            Console.WriteLine("\n[TEST 1] GetHandoff: Trích xuất đúng prompt snapshot và expectedOutputSchema...");
            var now = DateTimeOffset.UtcNow.ToString("o");

            using (var db = new TiToAIDbContext(dbOptions))
            {
                // Job Script (scene_id = null)
                db.Jobs.Add(new Job
                {
                    Id = "job_test1_script",
                    WorkspaceId = "ws_handoff",
                    ProjectId = "prj_handoff",
                    SceneId = null,
                    TaskType = JobTaskType.Script,
                    State = JobState.AwaitingUser,
                    ReasonCode = JobReasonCode.AutomationBlocked,
                    ProviderKey = "chatgpt-web",
                    PromptSnapshotText = JsonSerializer.Serialize(new { RenderedText = "Prompt kịch bản 30s về TiToAI" }),
                    ConfigSnapshotJson = JsonSerializer.Serialize(new { workflowVersion = "v1.2", maxAttempts = 3 }),
                    StorageRootId = "root_doc",
                    FencingToken = 10,
                    SideEffectSubmitted = 1,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });

                // Job VideoPrompt (scene_id != null)
                db.Jobs.Add(new Job
                {
                    Id = "job_test1_vidprompt",
                    WorkspaceId = "ws_handoff",
                    ProjectId = "prj_handoff",
                    SceneId = "scene_handoff_01",
                    TaskType = JobTaskType.VideoPrompt,
                    State = JobState.AwaitingUser,
                    ProviderKey = "gemini-web",
                    PromptSnapshotText = "Prompt chuyển động fly-cam mượt mà",
                    StorageRootId = "root_doc",
                    FencingToken = 12,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });

                // Job Image (scene_id != null)
                db.Jobs.Add(new Job
                {
                    Id = "job_test1_image",
                    WorkspaceId = "ws_handoff",
                    ProjectId = "prj_handoff",
                    SceneId = "scene_handoff_01",
                    TaskType = JobTaskType.Image,
                    State = JobState.AwaitingUser,
                    ProviderKey = "chatgpt-web",
                    PromptSnapshotText = "Photorealistic 8k studio shot",
                    StorageRootId = "root_img",
                    FencingToken = 15,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });

                // Job VideoRender (scene_id != null)
                db.Jobs.Add(new Job
                {
                    Id = "job_test1_vidrender",
                    WorkspaceId = "ws_handoff",
                    ProjectId = "prj_handoff",
                    SceneId = "scene_handoff_01",
                    TaskType = JobTaskType.VideoRender,
                    State = JobState.AwaitingUser,
                    ProviderKey = "museai",
                    PromptSnapshotText = "Render thông số 1080p 30fps",
                    StorageRootId = "root_vid",
                    FencingToken = 20,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });

                db.SaveChanges();
            }

            // Kiểm tra Script handoff
            var scriptHandoff = (await service.GetHandoffAsync(new JobGetHandoffRequest("ws_handoff", "job_test1_script"))).Handoff;
            Assert(scriptHandoff.ExpectedOutputSchema == "script_json_v1", "Script schema phải là 'script_json_v1'");
            Assert(scriptHandoff.PromptText == "Prompt kịch bản 30s về TiToAI", "Prompt sạch phải được trích xuất từ RenderedText");
            Assert(scriptHandoff.WorkflowVersion == "v1.2", "WorkflowVersion phải được trích xuất từ ConfigSnapshot");
            Assert(scriptHandoff.HasUnresolvedSideEffect == true, "SideEffectSubmitted == 1 chưa có kết quả phải báo hasUnresolvedSideEffect");
            Assert(scriptHandoff.Instructions.Contains("JSON"), "Hướng dẫn kịch bản phải đề cập đến JSON");

            // Kiểm tra VideoPrompt handoff
            var vidPromptHandoff = (await service.GetHandoffAsync(new JobGetHandoffRequest("ws_handoff", "job_test1_vidprompt"))).Handoff;
            Assert(vidPromptHandoff.ExpectedOutputSchema == "text_v1", "VideoPrompt schema phải là 'text_v1'");
            Assert(vidPromptHandoff.PromptText == "Prompt chuyển động fly-cam mượt mà", "Prompt raw text phải giữ nguyên");

            // Kiểm tra Image handoff
            var imgHandoff = (await service.GetHandoffAsync(new JobGetHandoffRequest("ws_handoff", "job_test1_image"))).Handoff;
            Assert(imgHandoff.ExpectedOutputSchema == "image_asset", "Image schema phải là 'image_asset'");

            // Kiểm tra VideoRender handoff
            var renderHandoff = (await service.GetHandoffAsync(new JobGetHandoffRequest("ws_handoff", "job_test1_vidrender"))).Handoff;
            Assert(renderHandoff.ExpectedOutputSchema == "video_asset", "VideoRender schema phải là 'video_asset'");

            Console.WriteLine("  -> PASS: Trích xuất chính xác prompt sạch, workflowVersion và schema theo từng loại task.");

            // -------------------------------------------------------------------------
            // TEST 2: Import Script JSON hợp lệ thành công và chuyển Succeeded, giải phóng slot occupancy
            // -------------------------------------------------------------------------
            Console.WriteLine("\n[TEST 2] Import Script: JSON hợp lệ -> chuyển Succeeded và giải phóng ActiveSlotOccupancy...");
            using (var db = new TiToAIDbContext(dbOptions))
            {
                db.Jobs.Add(new Job
                {
                    Id = "job_test2_script",
                    WorkspaceId = "ws_handoff",
                    ProjectId = "prj_handoff",
                    SceneId = null,
                    TaskType = JobTaskType.Script,
                    State = JobState.Running,
                    ProviderKey = "chatgpt-web",
                    StorageRootId = "root_doc",
                    FencingToken = 5,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });

                db.ActiveSlotOccupancies.Add(new ActiveSlotOccupancy
                {
                    ProviderAccountId = "acc_handoff",
                    TabSlotId = "tab_01",
                    JobId = "job_test2_script",
                    LeaseOwnerId = "worker_01",
                    LeaseToken = 6,
                    AcquiredAtUtc = now
                });

                db.SaveChanges();
            }

            var validScriptJson = JsonSerializer.Serialize(new
            {
                title = "Kịch bản giới thiệu TiToAI AI",
                scenes = new[]
                {
                    new
                    {
                        key = "scene_intro",
                        order = 1,
                        name = "Cảnh mở đầu",
                        description = "Logo TiToAI phát sáng huyền ảo",
                        targetDurationSeconds = 6.5,
                        imagePrompt = "Glowing neon logo TiToAI with futuristic particles"
                    },
                    new
                    {
                        key = "scene_feature",
                        order = 2,
                        name = "Cảnh tính năng",
                        description = "Giao diện quản lý hàng đợi mượt mà",
                        targetDurationSeconds = 12.0,
                        imagePrompt = "Modern dashboard UI with dark glassmorphism style"
                    }
                }
            });

            var importRes2 = await service.ImportResultAsync(new JobImportResultRequest(
                WorkspaceId: "ws_handoff",
                JobId: "job_test2_script",
                ExpectedRevision: 5,
                ClientRequestId: "req_test2_001",
                TextResult: validScriptJson
            ));

            Assert(importRes2.State == JobState.Succeeded, "Trạng thái sau import phải là 'Succeeded'");
            Assert(importRes2.Revision == 6, "Revision (FencingToken) phải tăng lên 6");
            Assert(importRes2.IsDuplicate == false, "IsDuplicate lần đầu phải là false");
            Assert(importRes2.WasCanceled == false, "WasCanceled phải là false");

            // Kiểm tra DB
            using (var db = new TiToAIDbContext(dbOptions))
            {
                var job2 = db.Jobs.First(j => j.Id == "job_test2_script");
                Assert(job2.State == JobState.Succeeded, "DB: Job phải là Succeeded");
                Assert(job2.FencingToken == 6, "DB: FencingToken phải là 6");
                Assert(!string.IsNullOrEmpty(job2.ResultText), "DB: ResultText phải được lưu trữ");
                Assert(job2.ResultText!.Contains("\"schemaVersion\":1"), "DB: ResultText phải chứa schemaVersion: 1");

                var occupancy2 = db.ActiveSlotOccupancies.FirstOrDefault(o => o.JobId == "job_test2_script");
                Assert(occupancy2 == null, "DB: ActiveSlotOccupancy phải được giải phóng hoàn toàn!");
            }

            Console.WriteLine("  -> PASS: Import Script thành công, lưu ResultText và giải phóng Slot Occupancy nguyên tử.");

            // -------------------------------------------------------------------------
            // TEST 3: Import Script sai format bị từ chối với JobValidationException
            // -------------------------------------------------------------------------
            Console.WriteLine("\n[TEST 3] Import Script: Cấu trúc JSON không hợp lệ -> Từ chối với JobValidationException...");
            using (var db = new TiToAIDbContext(dbOptions))
            {
                db.Jobs.Add(new Job
                {
                    Id = "job_test3_validate",
                    WorkspaceId = "ws_handoff",
                    ProjectId = "prj_handoff",
                    SceneId = null,
                    TaskType = JobTaskType.Script,
                    State = JobState.Running,
                    ProviderKey = "chatgpt-web",
                    StorageRootId = "root_doc",
                    FencingToken = 10,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
                db.SaveChanges();
            }

            // 3a. Thiếu title
            bool threwNoTitle = false;
            try
            {
                await service.ImportResultAsync(new JobImportResultRequest(
                    WorkspaceId: "ws_handoff",
                    JobId: "job_test3_validate",
                    ExpectedRevision: 10,
                    ClientRequestId: "req_test3_notitle",
                    TextResult: "{\"scenes\": []}"
                ));
            }
            catch (JobValidationException ex)
            {
                threwNoTitle = true;
                Assert(ex.Message.Contains("title"), "Thông báo lỗi phải đề cập đến 'title'");
            }
            Assert(threwNoTitle, "Phải ném JobValidationException khi thiếu title");

            // 3b. Mảng scenes rỗng
            bool threwEmptyScenes = false;
            try
            {
                await service.ImportResultAsync(new JobImportResultRequest(
                    WorkspaceId: "ws_handoff",
                    JobId: "job_test3_validate",
                    ExpectedRevision: 10,
                    ClientRequestId: "req_test3_emptyscenes",
                    TextResult: "{\"title\": \"Tiêu đề\", \"scenes\": []}"
                ));
            }
            catch (JobValidationException ex)
            {
                threwEmptyScenes = true;
                Assert(ex.Message.Contains("scenes"), "Thông báo lỗi phải đề cập đến 'scenes'");
            }
            Assert(threwEmptyScenes, "Phải ném JobValidationException khi scenes rỗng");

            // 3c. Order không liên tục từ 1 (ví dụ order = 3 ở scene đầu tiên)
            bool threwBadOrder = false;
            try
            {
                var badOrderJson = JsonSerializer.Serialize(new
                {
                    title = "Test Order",
                    scenes = new[]
                    {
                        new { key = "sc_01", order = 3, name = "Cảnh 1", targetDurationSeconds = 10.0 }
                    }
                });
                await service.ImportResultAsync(new JobImportResultRequest(
                    WorkspaceId: "ws_handoff",
                    JobId: "job_test3_validate",
                    ExpectedRevision: 10,
                    ClientRequestId: "req_test3_badorder",
                    TextResult: badOrderJson
                ));
            }
            catch (JobValidationException ex)
            {
                threwBadOrder = true;
                Assert(ex.Message.Contains("order"), "Thông báo lỗi phải đề cập đến 'order'");
            }
            Assert(threwBadOrder, "Phải ném JobValidationException khi order không bắt đầu từ 1");

            // 3d. Duration âm hoặc bằng 0
            bool threwBadDuration = false;
            try
            {
                var badDurationJson = JsonSerializer.Serialize(new
                {
                    title = "Test Duration",
                    scenes = new[]
                    {
                        new { key = "sc_01", order = 1, name = "Cảnh 1", targetDurationSeconds = -5.0 }
                    }
                });
                await service.ImportResultAsync(new JobImportResultRequest(
                    WorkspaceId: "ws_handoff",
                    JobId: "job_test3_validate",
                    ExpectedRevision: 10,
                    ClientRequestId: "req_test3_baddur",
                    TextResult: badDurationJson
                ));
            }
            catch (JobValidationException ex)
            {
                threwBadDuration = true;
                Assert(ex.Message.Contains("targetDurationSeconds"), "Thông báo lỗi phải đề cập đến targetDurationSeconds");
            }
            Assert(threwBadDuration, "Phải ném JobValidationException khi duration âm");

            Console.WriteLine("  -> PASS: Bắt trọn và từ chối các trường hợp sai cấu trúc Script JSON với JobValidationException rõ ràng.");

            // -------------------------------------------------------------------------
            // TEST 4: Import VideoPrompt text hợp lệ thành công
            // -------------------------------------------------------------------------
            Console.WriteLine("\n[TEST 4] Import VideoPrompt: Nhập text prompt chuyển động video thành công...");
            using (var db = new TiToAIDbContext(dbOptions))
            {
                db.Jobs.Add(new Job
                {
                    Id = "job_test4_vidprompt",
                    WorkspaceId = "ws_handoff",
                    ProjectId = "prj_handoff",
                    SceneId = "scene_handoff_01",
                    TaskType = JobTaskType.VideoPrompt,
                    State = JobState.Running,
                    ProviderKey = "chatgpt-web",
                    StorageRootId = "root_doc",
                    FencingToken = 25,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
                db.SaveChanges();
            }

            var promptTextResult = "Fly through high-tech server room with glowing blue fiber optics, cinematic 60fps";
            var importRes4 = await service.ImportResultAsync(new JobImportResultRequest(
                WorkspaceId: "ws_handoff",
                JobId: "job_test4_vidprompt",
                ExpectedRevision: 25,
                ClientRequestId: "req_test4_prompt",
                TextResult: promptTextResult
            ));

            Assert(importRes4.State == JobState.Succeeded, "Trạng thái phải là Succeeded");
            Assert(importRes4.Revision == 26, "Revision phải tăng lên 26");

            using (var db = new TiToAIDbContext(dbOptions))
            {
                var job4 = db.Jobs.First(j => j.Id == "job_test4_vidprompt");
                Assert(job4.State == JobState.Succeeded, "DB: Job phải là Succeeded");
                Assert(job4.ResultText!.Contains(promptTextResult), "DB: ResultText phải chứa prompt video");
            }

            Console.WriteLine("  -> PASS: Import VideoPrompt thành công và đóng gói đúng schema text_v1.");

            // -------------------------------------------------------------------------
            // TEST 5: Idempotency với cùng ClientRequestId trả kết quả cũ không tạo duplicate
            // -------------------------------------------------------------------------
            Console.WriteLine("\n[TEST 5] Idempotency: Gửi lại cùng ClientRequestId trả kết quả cũ (IsDuplicate = true)...");
            var duplicateRes = await service.ImportResultAsync(new JobImportResultRequest(
                WorkspaceId: "ws_handoff",
                JobId: "job_test2_script",
                ExpectedRevision: 5,
                ClientRequestId: "req_test2_001",
                TextResult: validScriptJson
            ));

            Assert(duplicateRes.IsDuplicate == true, "Lần gọi thứ hai với cùng ClientRequestId phải có IsDuplicate == true");
            Assert(duplicateRes.JobId == "job_test2_script", "Job ID phải khớp");
            Assert(duplicateRes.State == JobState.Succeeded, "State phải là Succeeded");

            // Kiểm tra gửi cùng ClientRequestId nhưng khác payload -> Bị chặn xung đột
            bool threwConflictOnDiffPayload = false;
            try
            {
                await service.ImportResultAsync(new JobImportResultRequest(
                    WorkspaceId: "ws_handoff",
                    JobId: "job_test2_script",
                    ExpectedRevision: 5,
                    ClientRequestId: "req_test2_001",
                    TextResult: "{\"title\": \"Khác hoàn toàn\"}"
                ));
            }
            catch (JobConcurrencyConflictException)
            {
                threwConflictOnDiffPayload = true;
            }
            Assert(threwConflictOnDiffPayload, "Cùng ClientRequestId khác payload phải ném JobConcurrencyConflictException");

            Console.WriteLine("  -> PASS: Đảm bảo tính lặp idempotent tuyệt đối và bảo vệ an toàn dữ liệu.");

            // -------------------------------------------------------------------------
            // TEST 6: Import kết quả sau khi Job đã Cancel giữ trạng thái Canceled, unlink asset, không ghi đè
            // -------------------------------------------------------------------------
            Console.WriteLine("\n[TEST 6] Late Result on Canceled Job: Giữ Canceled, chuyển Asset sang Unlinked...");
            using (var db = new TiToAIDbContext(dbOptions))
            {
                // Asset ban đầu là Linked
                db.Assets.Add(new Asset
                {
                    Id = "asset_img_late_01",
                    WorkspaceId = "ws_handoff",
                    ProjectId = "prj_handoff",
                    StorageRootId = "root_img",
                    RelativePath = "images/scene1.png",
                    AssetType = "Image",
                    Status = "Linked",
                    CreatedAtUtc = now
                });

                // Job đã bị Canceled
                db.Jobs.Add(new Job
                {
                    Id = "job_test6_canceled",
                    WorkspaceId = "ws_handoff",
                    ProjectId = "prj_handoff",
                    SceneId = "scene_handoff_01",
                    TaskType = JobTaskType.Image,
                    State = JobState.Canceled,
                    CancelRequested = 1,
                    ReasonCode = JobReasonCode.UserCanceled,
                    ProviderKey = "chatgpt-web",
                    StorageRootId = "root_img",
                    FencingToken = 30,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });

                db.ActiveSlotOccupancies.Add(new ActiveSlotOccupancy
                {
                    ProviderAccountId = "acc_handoff",
                    TabSlotId = "tab_canceled",
                    JobId = "job_test6_canceled",
                    LeaseOwnerId = "worker_canceled",
                    LeaseToken = 31,
                    AcquiredAtUtc = now
                });

                db.SaveChanges();
            }

            var lateResultRes = await service.ImportResultAsync(new JobImportResultRequest(
                WorkspaceId: "ws_handoff",
                JobId: "job_test6_canceled",
                ExpectedRevision: 30,
                ClientRequestId: "req_test6_late",
                AssetId: "asset_img_late_01"
            ));

            Assert(lateResultRes.WasCanceled == true, "WasCanceled phải là true");
            Assert(lateResultRes.State == JobState.Canceled, "State phải giữ nguyên Canceled (TUYỆT ĐỐI KHÔNG Succeeded)");
            Assert(lateResultRes.AssetId == "asset_img_late_01", "AssetId phải được lưu");

            using (var db = new TiToAIDbContext(dbOptions))
            {
                var canceledJob = db.Jobs.First(j => j.Id == "job_test6_canceled");
                Assert(canceledJob.State == JobState.Canceled, "DB: Job phải giữ nguyên trạng thái Canceled");
                Assert(canceledJob.ResultReference == "asset_img_late_01", "DB: ResultReference phải lưu assetId");

                var asset = db.Assets.First(a => a.Id == "asset_img_late_01");
                Assert(asset.Status == "Unlinked", "DB: Asset phải được chuyển sang 'Unlinked' để bảo toàn an toàn!");

                var occupancy = db.ActiveSlotOccupancies.FirstOrDefault(o => o.JobId == "job_test6_canceled");
                Assert(occupancy == null, "DB: ActiveSlotOccupancy phải được giải phóng");
            }

            Console.WriteLine("  -> PASS: Tác vụ đã hủy nhận kết quả giữ nguyên Canceled, Asset đánh dấu Unlinked, Slot giải phóng an toàn.");

            // -------------------------------------------------------------------------
            // TEST 7: Concurrency conflict (sai expectedRevision) bị chặn
            // -------------------------------------------------------------------------
            Console.WriteLine("\n[TEST 7] Optimistic Concurrency: Stale ExpectedRevision bị chặn bằng JobConcurrencyConflictException...");
            using (var db = new TiToAIDbContext(dbOptions))
            {
                db.Jobs.Add(new Job
                {
                    Id = "job_test7_stale",
                    WorkspaceId = "ws_handoff",
                    ProjectId = "prj_handoff",
                    SceneId = "scene_handoff_01",
                    TaskType = JobTaskType.VideoPrompt,
                    State = JobState.Running,
                    ProviderKey = "chatgpt-web",
                    StorageRootId = "root_doc",
                    FencingToken = 100, // DB revision là 100
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
                db.SaveChanges();
            }

            bool threwConcurrencyConflict = false;
            try
            {
                // UI gửi với expectedRevision = 99 (stale token)
                await service.ImportResultAsync(new JobImportResultRequest(
                    WorkspaceId: "ws_handoff",
                    JobId: "job_test7_stale",
                    ExpectedRevision: 99,
                    ClientRequestId: "req_test7_conflict",
                    TextResult: "Some prompt text"
                ));
            }
            catch (JobConcurrencyConflictException ex)
            {
                threwConcurrencyConflict = true;
                Assert(ex.Message.Contains("Revision"), "Thông báo lỗi phải đề cập đến Revision");
            }

            Assert(threwConcurrencyConflict, "Bắt buộc phải ném JobConcurrencyConflictException khi revision không khớp!");
            Console.WriteLine("  -> PASS: Stale revision bị chặn đứng, bảo vệ toàn vẹn dữ liệu đa luồng.");

            Console.WriteLine("\n================================================================================");
            Console.WriteLine(" TẤT CẢ 7/7 KIỂM THỬ KHÓI MANUAL HANDOFF ĐÃ VƯỢT QUA XUẤT SẮC (PASS)!");
            Console.WriteLine("================================================================================\n");

            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[FATAL ERROR TRONG SMOKE TESTS]: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Console.ResetColor();
            return 1;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch
            {
                // Bỏ qua lỗi dọn file tạm
            }
        }
    }
}
