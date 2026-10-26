using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TiToAI.Application.Accounts;
using TiToAI.Application.Workspaces;
using TiToAI.Contracts.Accounts;
using TiToAI.Contracts.Workspaces;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Persistence.Entities;
using TiToAI.Infrastructure.Persistence.Interceptors;

namespace TiToAI.Infrastructure.SmokeChecks;

/// <summary>
/// Bộ kiểm thử khói kiểm chứng nghiệp vụ Backend cho Task T07 (T07a Workspace CRUD & Pause, T07b Account Linking)
/// thực thi trên SQLite DB vật lý tạm thời với đầy đủ PRAGMA WAL và Foreign Keys.
/// </summary>
public static class WorkspaceAndAccountSmokeTests
{
    public sealed class TestProfileIdentityResolver : IProfileIdentityResolver
    {
        private readonly Dictionary<(string ProfileId, string Provider), ProfileIdentityResolutionResult> _results = new();

        public void SetupResult(string profileId, string provider, ProfileIdentityResolutionResult result)
        {
            _results[(profileId, provider)] = result;
        }

        public Task<ProfileIdentityResolutionResult> ResolveProfileIdentityAsync(
            string profileId,
            string provider,
            CancellationToken cancellationToken = default)
        {
            if (_results.TryGetValue((profileId, provider), out var res))
            {
                return Task.FromResult(res);
            }

            return Task.FromResult(ProfileIdentityResolutionResult.Failed($"Chưa cấu hình hồ sơ '{profileId}' cho nhà cung cấp '{provider}'."));
        }
    }

    public sealed class FailingWorkspaceDeleteInterceptor : DbCommandInterceptor
    {
        public bool HasTriggered { get; private set; }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result)
        {
            if (command.CommandText.Contains("DELETE FROM \"Workspace\"", StringComparison.OrdinalIgnoreCase) ||
                command.CommandText.Contains("DELETE FROM Workspace", StringComparison.OrdinalIgnoreCase))
            {
                HasTriggered = true;
                throw new InvalidOperationException("Mô phỏng sự cố IO / phần cứng giữa chừng trong transaction!");
            }

            return base.NonQueryExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("DELETE FROM \"Workspace\"", StringComparison.OrdinalIgnoreCase) ||
                command.CommandText.Contains("DELETE FROM Workspace", StringComparison.OrdinalIgnoreCase))
            {
                HasTriggered = true;
                throw new InvalidOperationException("Mô phỏng sự cố IO / phần cứng giữa chừng trong transaction!");
            }

            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            if (command.CommandText.Contains("DELETE FROM \"Workspace\"", StringComparison.OrdinalIgnoreCase) ||
                command.CommandText.Contains("DELETE FROM Workspace", StringComparison.OrdinalIgnoreCase))
            {
                HasTriggered = true;
                throw new InvalidOperationException("Mô phỏng sự cố IO / phần cứng giữa chừng trong transaction!");
            }

            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("DELETE FROM \"Workspace\"", StringComparison.OrdinalIgnoreCase) ||
                command.CommandText.Contains("DELETE FROM Workspace", StringComparison.OrdinalIgnoreCase))
            {
                HasTriggered = true;
                throw new InvalidOperationException("Mô phỏng sự cố IO / phần cứng giữa chừng trong transaction!");
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    public static async Task RunTestsAsync(string databasePath)
    {
        Console.WriteLine("\n=================================================================");
        Console.WriteLine("    BẮT ĐẦU KIỂM THỬ KHÓI T07: WORKSPACE & ACCOUNT BACKEND      ");
        Console.WriteLine("=================================================================");

        var connectionString = DatabasePathHelper.BuildConnectionString(databasePath);
        var optionsBuilder = new DbContextOptionsBuilder<TiToAIDbContext>();
        optionsBuilder.UseSqlite(connectionString)
                      .AddInterceptors(new SqlitePragmaInterceptor());
        var dbOptions = optionsBuilder.Options;

        var tempStorageRoot = Path.Combine(Path.GetTempPath(), "titoai_ws_storage_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempStorageRoot);

        try
        {
            var workspaceService = new WorkspaceService(dbOptions, tempStorageRoot);
            var fakeResolver = new TestProfileIdentityResolver();
            var accountService = new AccountLinkService(dbOptions, fakeResolver);

            await TestWorkspaceCrudAndValidationAsync(workspaceService);
            await TestWorkspacePauseSemanticsAsync(workspaceService);
            await TestWorkspaceDeleteGuardsAndSafeDetachAsync(workspaceService, dbOptions, tempStorageRoot);
            await TestRaceConditionBetweenDeleteAndEnqueueAsync(workspaceService, dbOptions);
            await TestTransactionRollbackOnDeleteFailureAsync(connectionString, tempStorageRoot);
            await TestAssetDetachPreservesNoActionConstraintAsync(workspaceService, dbOptions);
            await TestConservativeSideEffectReconciliationGuardAsync(workspaceService, dbOptions);
            await TestAccountLinkingAndGuardsAsync(accountService, workspaceService, fakeResolver);
            await TestCrossWorkspaceSecurityAndRelinkAsync(accountService, workspaceService, fakeResolver);

            Console.WriteLine("\n>>> TOÀN BỘ CÁC KIỂM THỬ KHÓI CHO T07 ĐÃ VƯỢT QUA THÀNH CÔNG (PASS)! <<<");
        }
        finally
        {
            if (Directory.Exists(tempStorageRoot))
            {
                try { Directory.Delete(tempStorageRoot, true); } catch { }
            }
        }
    }

    private static async Task TestWorkspaceCrudAndValidationAsync(WorkspaceService workspaceService)
    {
        Console.WriteLine("\n[T07a.1] Kiểm tra Tạo Không gian làm việc & Validation...");

        // Validation: Tên rỗng
        try
        {
            await workspaceService.CreateAsync(new WorkspaceCreateRequest("", "@valid_handle", "Topic"));
            throw new InvalidOperationException("Cần quăng lỗi khi Tên rỗng!");
        }
        catch (WorkspaceValidationException)
        {
            Console.WriteLine("    -> Chặn tạo khi Tên rỗng: PASS");
        }

        // Validation: ChannelHandle rỗng
        try
        {
            await workspaceService.CreateAsync(new WorkspaceCreateRequest("Kênh 1", "   ", "Topic"));
            throw new InvalidOperationException("Cần quăng lỗi khi ChannelHandle rỗng!");
        }
        catch (WorkspaceValidationException)
        {
            Console.WriteLine("    -> Chặn tạo khi ChannelHandle rỗng: PASS");
        }

        // Validation: Topic rỗng
        try
        {
            await workspaceService.CreateAsync(new WorkspaceCreateRequest("Kênh 1", "@handle", ""));
            throw new InvalidOperationException("Cần quăng lỗi khi Topic rỗng!");
        }
        catch (WorkspaceValidationException)
        {
            Console.WriteLine("    -> Chặn tạo khi Topic rỗng: PASS");
        }

        // Tạo thành công (handle tự động thêm tiền tố @ nếu thiếu)
        var createRes = await workspaceService.CreateAsync(new WorkspaceCreateRequest("Kênh Review Phim", "reviewphim_official", "Phim điện ảnh 2026"));
        Assert(!string.IsNullOrWhiteSpace(createRes.WorkspaceId), "WorkspaceId phải có giá trị");
        Assert(Guid.TryParse(createRes.WorkspaceId, out _), "WorkspaceId phải là GUID hợp lệ");
        Console.WriteLine($"    -> Tạo Workspace thành công: ID = {createRes.WorkspaceId}");

        // Đọc chi tiết
        var detail = await workspaceService.GetAsync(createRes.WorkspaceId);
        Assert(detail.Name == "Kênh Review Phim", "Tên kênh không khớp");
        Assert(detail.ChannelHandle == "@reviewphim_official", "Handle phải tự động thêm @");
        Assert(detail.Topic == "Phim điện ảnh 2026", "Chủ đề không khớp");
        Assert(detail.Status == "Active", "Trạng thái ban đầu phải là Active");
        Console.WriteLine("    -> GetAsync xác minh đầy đủ thông tin: PASS");

        // Cập nhật Workspace
        var updateRes = await workspaceService.UpdateAsync(new WorkspaceUpdateRequest(
            createRes.WorkspaceId,
            "Kênh Review Phim Chiếu Rạp",
            "@reviewphim_rap",
            "Phim rạp chiếu 2026"));
        Assert(updateRes.Name == "Kênh Review Phim Chiếu Rạp", "Tên sau cập nhật không khớp");
        Assert(updateRes.ChannelHandle == "@reviewphim_rap", "Handle sau cập nhật không khớp");
        Console.WriteLine("    -> UpdateAsync cập nhật thành công: PASS");

        // List Workspaces
        var list = await workspaceService.ListAsync();
        Assert(list.Any(w => w.WorkspaceId == createRes.WorkspaceId), "Không tìm thấy Workspace trong danh sách list");
        Console.WriteLine($"    -> ListAsync tìm thấy {list.Count} không gian làm việc: PASS");
    }

    private static async Task TestWorkspacePauseSemanticsAsync(WorkspaceService workspaceService)
    {
        Console.WriteLine("\n[T07a.2] Kiểm tra Ngữ nghĩa Tạm dừng (Pause Semantics)...");

        var createRes = await workspaceService.CreateAsync(new WorkspaceCreateRequest("Kênh Tin Nóng", "@tinnong", "Tin tức"));
        var wsId = createRes.WorkspaceId;

        // Chuyển sang Paused
        var pauseRes = await workspaceService.SetStatusAsync(new WorkspaceSetStatusRequest(wsId, "Paused"));
        Assert(pauseRes.Status == "Paused", "Trạng thái phải là Paused");

        var detailPaused = await workspaceService.GetAsync(wsId);
        Assert(detailPaused.Status == "Paused", "Trạng thái trong DB phải là Paused");
        Console.WriteLine("    -> Đổi trạng thái sang 'Paused': PASS");

        // Chuyển lại sang Active
        var activeRes = await workspaceService.SetStatusAsync(new WorkspaceSetStatusRequest(wsId, "Active"));
        Assert(activeRes.Status == "Active", "Trạng thái phải là Active");

        var detailActive = await workspaceService.GetAsync(wsId);
        Assert(detailActive.Status == "Active", "Trạng thái trong DB phải là Active");
        Console.WriteLine("    -> Đổi trạng thái trở lại 'Active': PASS");

        // Thử trạng thái không hợp lệ
        try
        {
            await workspaceService.SetStatusAsync(new WorkspaceSetStatusRequest(wsId, "Archived"));
            throw new InvalidOperationException("Phải từ chối trạng thái không hợp lệ!");
        }
        catch (WorkspaceValidationException)
        {
            Console.WriteLine("    -> Từ chối trạng thái không hợp lệ ('Archived'): PASS");
        }
    }

    private static async Task TestWorkspaceDeleteGuardsAndSafeDetachAsync(
        WorkspaceService workspaceService,
        DbContextOptions<TiToAIDbContext> dbOptions,
        string tempStorageRoot)
    {
        Console.WriteLine("\n[T07a.3] Kiểm tra Strict Nonterminal Delete Guard & Safe Detach Asset...");

        var createRes = await workspaceService.CreateAsync(new WorkspaceCreateRequest("Kênh Xóa Thử Nghiệm", "@deletetest", "Thử nghiệm"));
        var wsId = createRes.WorkspaceId;

        // Giả lập tạo Project, Scene, Job trong Workspace này
        await using (var db = new TiToAIDbContext(dbOptions))
        {
            var project = new Project
            {
                Id = Guid.NewGuid().ToString("D").ToLowerInvariant(),
                WorkspaceId = wsId,
                Name = "Project Test",
                CreatedAtUtc = DateTime.UtcNow.ToString("o"),
                UpdatedAtUtc = DateTime.UtcNow.ToString("o")
            };
            db.Projects.Add(project);

            var scene = new Scene
            {
                Id = Guid.NewGuid().ToString("D").ToLowerInvariant(),
                ProjectId = project.Id,
                WorkspaceId = wsId,
                SequenceOrder = 1,
                Name = "Scene 1",
                TargetDurationSeconds = 15,
                CreatedAtUtc = DateTime.UtcNow.ToString("o"),
                UpdatedAtUtc = DateTime.UtcNow.ToString("o")
            };
            db.Scenes.Add(scene);

            var runningJob = new Job
            {
                Id = Guid.NewGuid().ToString("D").ToLowerInvariant(),
                WorkspaceId = wsId,
                ProjectId = project.Id,
                SceneId = scene.Id,
                TaskType = "Script",
                State = "Running", // Nonterminal State!
                ProviderKey = "chatgpt-web",
                StorageRootId = "root-default-docs",
                CreatedAtUtc = DateTime.UtcNow.ToString("o"),
                UpdatedAtUtc = DateTime.UtcNow.ToString("o")
            };
            db.Jobs.Add(runningJob);

            await db.SaveChangesAsync();
        }

        // Thử xóa trong khi có Job ở trạng thái Running -> Phải bị chặn với CONCURRENCY_CONFLICT
        try
        {
            await workspaceService.DeleteAsync(wsId);
            throw new InvalidOperationException("Phải từ chối xóa khi có Job đang Running!");
        }
        catch (WorkspaceConcurrencyConflictException)
        {
            Console.WriteLine("    -> Strict Guard chặn xóa khi có Job Running: PASS");
        }

        // Cập nhật Job sang Succeeded và thêm Asset có ProjectId (liên kết composite ON DELETE NO ACTION)
        await using (var db = new TiToAIDbContext(dbOptions))
        {
            var job = await db.Jobs.FirstAsync(j => j.WorkspaceId == wsId);
            job.State = "Succeeded";

            var project = await db.Projects.FirstAsync(p => p.WorkspaceId == wsId);
            var asset = new Asset
            {
                Id = Guid.NewGuid().ToString("D").ToLowerInvariant(),
                WorkspaceId = wsId,
                ProjectId = project.Id, // Có ProjectId trỏ tới Project
                StorageRootId = "root-default-images",
                RelativePath = $"{wsId}/projects/{project.Id}/images/test.png",
                AssetType = "Image",
                ByteSize = 1024,
                Sha256Hash = "abc123hash",
                Status = "Linked",
                CreatedAtUtc = DateTime.UtcNow.ToString("o")
            };
            db.Assets.Add(asset);

            await db.SaveChangesAsync();
        }

        // Tạo thư mục subtree giả lập cho workspace
        var wsDir = Path.Combine(tempStorageRoot, wsId);
        Directory.CreateDirectory(wsDir);
        File.WriteAllText(Path.Combine(wsDir, "dummy.txt"), "hello world");
        Assert(Directory.Exists(wsDir), "Thư mục workspace phải tồn tại trước khi xóa");

        // Thực hiện xóa Workspace an toàn
        var deleteRes = await workspaceService.DeleteAsync(wsId);
        Assert(deleteRes.Deleted, "Kết quả xóa phải là true");
        Console.WriteLine("    -> Safe Detach Delete Transaction hoàn tất thành công: PASS");

        // Kiểm tra Workspace đã bị xóa khỏi DB
        await using (var db = new TiToAIDbContext(dbOptions))
        {
            var wsExists = await db.Workspaces.AnyAsync(w => w.Id == wsId);
            Assert(!wsExists, "Workspace phải không còn trong DB");

            var projectsExist = await db.Projects.AnyAsync(p => p.WorkspaceId == wsId);
            Assert(!projectsExist, "Projects thuộc Workspace phải bị cascade xóa sạch");

            var assetsExist = await db.Assets.AnyAsync(a => a.WorkspaceId == wsId);
            Assert(!assetsExist, "Assets thuộc Workspace phải bị xóa sạch mà không vướng NO ACTION");
        }
        Console.WriteLine("    -> Cascade dọn dẹp sạch sẽ không lỗi FK: PASS");

        // Kiểm tra thư mục subtree đã được dọn dẹp
        Assert(!Directory.Exists(wsDir), "Thư mục subtree của workspace phải được dọn dẹp");
        Console.WriteLine("    -> Dọn dẹp thư mục subtree an toàn: PASS");
    }

    private static async Task TestRaceConditionBetweenDeleteAndEnqueueAsync(
        WorkspaceService workspaceService,
        DbContextOptions<TiToAIDbContext> dbOptions)
    {
        Console.WriteLine("\n[T07a.4] Kiểm tra Race Condition giữa Delete và Enqueue (Write Lock Serialization)...");

        for (int i = 0; i < 3; i++)
        {
            var wsRes = await workspaceService.CreateAsync(new WorkspaceCreateRequest(
                $"Race WS {i}", $"@race_ws_{i}", "Concurrency Testing"));
            var wsId = wsRes.WorkspaceId;
            var projId = Guid.NewGuid().ToString("D").ToLowerInvariant();
            var sceneId = Guid.NewGuid().ToString("D").ToLowerInvariant();

            await using (var db = new TiToAIDbContext(dbOptions))
            {
                var proj = new Project
                {
                    Id = projId,
                    WorkspaceId = wsId,
                    Name = $"Project {i}",
                    CreatedAtUtc = DateTime.UtcNow.ToString("o"),
                    UpdatedAtUtc = DateTime.UtcNow.ToString("o")
                };
                db.Projects.Add(proj);

                var scene = new Scene
                {
                    Id = sceneId,
                    ProjectId = projId,
                    WorkspaceId = wsId,
                    SequenceOrder = 1,
                    Name = "Scene 1",
                    TargetDurationSeconds = 10,
                    CreatedAtUtc = DateTime.UtcNow.ToString("o"),
                    UpdatedAtUtc = DateTime.UtcNow.ToString("o")
                };
                db.Scenes.Add(scene);
                await db.SaveChangesAsync();
            }

            var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            bool deleteSucceeded = false;
            bool deleteConflict = false;
            bool enqueueSucceeded = false;
            bool enqueueFailed = false;

            var deleteTask = Task.Run(async () =>
            {
                await startGate.Task;
                try
                {
                    await workspaceService.DeleteAsync(wsId);
                    deleteSucceeded = true;
                }
                catch (WorkspaceConcurrencyConflictException)
                {
                    deleteConflict = true;
                }
            });

            var enqueueTask = Task.Run(async () =>
            {
                await startGate.Task;
                try
                {
                    await using var db = new TiToAIDbContext(dbOptions);
                    var newJob = new Job
                    {
                        Id = Guid.NewGuid().ToString("D").ToLowerInvariant(),
                        WorkspaceId = wsId,
                        ProjectId = projId,
                        SceneId = sceneId,
                        TaskType = "Script",
                        State = "Queued",
                        ProviderKey = "chatgpt-web",
                        StorageRootId = "root-default-docs",
                        CreatedAtUtc = DateTime.UtcNow.ToString("o"),
                        UpdatedAtUtc = DateTime.UtcNow.ToString("o")
                    };
                    db.Jobs.Add(newJob);
                    await db.SaveChangesAsync();
                    enqueueSucceeded = true;
                }
                catch
                {
                    enqueueFailed = true;
                }
            });

            startGate.SetResult();
            await Task.WhenAll(deleteTask, enqueueTask);

            // Kiểm tra tính nhất quán (Consistent State):
            // Nhờ SQLite write lock serialize (BEGIN IMMEDIATE), chỉ có 2 kịch bản an toàn:
            // 1. Delete hoàn tất trước -> Workspace không còn, Enqueue bị chặn / thất bại do FK.
            // 2. Enqueue hoàn tất trước -> Delete phát hiện có Queued Job trong write transaction và bị chặn với ConcurrencyConflict.
            await using (var verifyDb = new TiToAIDbContext(dbOptions))
            {
                var wsExists = await verifyDb.Workspaces.AnyAsync(w => w.Id == wsId);
                var jobExists = await verifyDb.Jobs.AnyAsync(j => j.WorkspaceId == wsId);

                if (deleteSucceeded)
                {
                    Assert(enqueueFailed || !jobExists, "Khi delete hoàn tất trước, enqueue phải bị từ chối hoặc job không được tồn tại.");
                    Assert(!wsExists, "Workspace phải không còn tồn tại sau khi delete thành công.");
                    Assert(!jobExists, "Không được có job mồ côi tồn tại khi workspace đã bị xóa.");
                }
                else if (deleteConflict)
                {
                    Assert(enqueueSucceeded, "Khi delete bị conflict, enqueue phải đã hoàn tất thành công trước.");
                    Assert(wsExists, "Workspace phải còn nguyên vẹn khi delete bị xung đột guard.");
                    Assert(jobExists, "Job phải tồn tại trong DB khi delete bị conflict.");
                }
                else
                {
                    throw new InvalidOperationException("Trạng thái bất định giữa Delete và Enqueue!");
                }
            }
        }

        Console.WriteLine("    -> Write lock serialize loại bỏ hoàn toàn race condition Delete/Enqueue: PASS");
    }

    private static async Task TestTransactionRollbackOnDeleteFailureAsync(
        string connectionString,
        string tempStorageRoot)
    {
        Console.WriteLine("\n[T07a.5] Kiểm tra Transaction Rollback khi quá trình xóa gặp lỗi giữa chừng...");

        var normalOptions = new DbContextOptionsBuilder<TiToAIDbContext>()
            .UseSqlite(connectionString)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;

        var normalWorkspaceService = new WorkspaceService(normalOptions, tempStorageRoot);
        var wsRes = await normalWorkspaceService.CreateAsync(new WorkspaceCreateRequest(
            "Kênh Rollback Test", "@rollback_test", "Kiểm tra Rollback"));
        var wsId = wsRes.WorkspaceId;
        var projId = Guid.NewGuid().ToString("D").ToLowerInvariant();
        var assetId = Guid.NewGuid().ToString("D").ToLowerInvariant();

        // Tạo Project và Asset liên kết
        await using (var db = new TiToAIDbContext(normalOptions))
        {
            var project = new Project
            {
                Id = projId,
                WorkspaceId = wsId,
                Name = "Project Rollback",
                CreatedAtUtc = DateTime.UtcNow.ToString("o"),
                UpdatedAtUtc = DateTime.UtcNow.ToString("o")
            };
            db.Projects.Add(project);

            var asset = new Asset
            {
                Id = assetId,
                WorkspaceId = wsId,
                ProjectId = projId, // Có ProjectId liên kết
                StorageRootId = "root-default-images",
                RelativePath = $"{wsId}/projects/{projId}/images/{assetId}.png",
                AssetType = "Image",
                ByteSize = 2048,
                Sha256Hash = "rollbackhash123",
                Status = "Linked",
                CreatedAtUtc = DateTime.UtcNow.ToString("o")
            };
            db.Assets.Add(asset);

            await db.SaveChangesAsync();
        }

        // Tạo Workspace folder trên đĩa để kiểm tra không bị dọn dẹp nhầm khi rollback
        var wsDir = Path.Combine(tempStorageRoot, wsId);
        Directory.CreateDirectory(wsDir);
        File.WriteAllText(Path.Combine(wsDir, "keep_alive.txt"), "data");

        // Cấu hình interceptor cố tình ném lỗi khi xóa Workspace (sau khi Asset đã được detach trong transaction)
        var failingInterceptor = new FailingWorkspaceDeleteInterceptor();
        var failingOptions = new DbContextOptionsBuilder<TiToAIDbContext>()
            .UseSqlite(connectionString)
            .AddInterceptors(new SqlitePragmaInterceptor(), failingInterceptor)
            .Options;

        var failingWorkspaceService = new WorkspaceService(failingOptions, tempStorageRoot);

        bool exceptionCaught = false;
        try
        {
            await failingWorkspaceService.DeleteAsync(wsId);
        }
        catch (Exception ex) when (ex.Message.Contains("Mô phỏng sự cố") || ex.InnerException?.Message.Contains("Mô phỏng sự cố") == true)
        {
            exceptionCaught = true;
        }

        Assert(exceptionCaught, "Phải ném ngoại lệ mô phỏng lỗi xóa giữa chừng.");
        Assert(failingInterceptor.HasTriggered, "Interceptor phải được kích hoạt.");

        // Xác minh Transaction Rollback:
        // 1. Workspace vẫn tồn tại nguyên vẹn
        // 2. Project vẫn tồn tại nguyên vẹn
        // 3. Asset vẫn tồn tại nguyên vẹn và ProjectId được phục hồi về ban đầu (không bị detach dở dang)
        // 4. Thư mục trên đĩa vẫn còn nguyên
        await using (var db = new TiToAIDbContext(normalOptions))
        {
            var ws = await db.Workspaces.FirstOrDefaultAsync(w => w.Id == wsId);
            Assert(ws != null, "Workspace phải vẫn tồn tại sau khi transaction bị rollback.");

            var proj = await db.Projects.FirstOrDefaultAsync(p => p.Id == projId);
            Assert(proj != null, "Project phải vẫn tồn tại sau khi transaction bị rollback.");

            var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == assetId);
            Assert(asset != null, "Asset phải vẫn tồn tại sau khi transaction bị rollback.");
            Assert(asset!.ProjectId == projId, $"Asset.ProjectId phải được rollback về '{projId}', thực tế là '{asset.ProjectId}'.");
        }

        Assert(Directory.Exists(wsDir), "Thư mục subtree không được bị xóa khi transaction rollback.");
        Console.WriteLine("    -> Transaction rollback bảo toàn nguyên vẹn Workspace và Asset ProjectId: PASS");
    }

    private static async Task TestAssetDetachPreservesNoActionConstraintAsync(
        WorkspaceService workspaceService,
        DbContextOptions<TiToAIDbContext> dbOptions)
    {
        Console.WriteLine("\n[T07a.6] Kiểm tra Asset detach bảo toàn ràng buộc ON DELETE NO ACTION giữa Asset và Project...");

        var wsRes = await workspaceService.CreateAsync(new WorkspaceCreateRequest(
            "Kênh NoAction Test", "@noaction_test", "Kiểm tra NO ACTION"));
        var wsId = wsRes.WorkspaceId;
        var projId = Guid.NewGuid().ToString("D").ToLowerInvariant();
        var linkedAssetId = Guid.NewGuid().ToString("D").ToLowerInvariant();
        var standaloneAssetId = Guid.NewGuid().ToString("D").ToLowerInvariant();

        await using (var db = new TiToAIDbContext(dbOptions))
        {
            var project = new Project
            {
                Id = projId,
                WorkspaceId = wsId,
                Name = "Project NoAction",
                CreatedAtUtc = DateTime.UtcNow.ToString("o"),
                UpdatedAtUtc = DateTime.UtcNow.ToString("o")
            };
            db.Projects.Add(project);

            // Asset 1: Gắn với Project
            db.Assets.Add(new Asset
            {
                Id = linkedAssetId,
                WorkspaceId = wsId,
                ProjectId = projId,
                StorageRootId = "root-default-images",
                RelativePath = $"{wsId}/projects/{projId}/images/{linkedAssetId}.png",
                AssetType = "Image",
                ByteSize = 100,
                Sha256Hash = "hash1",
                Status = "Linked",
                CreatedAtUtc = DateTime.UtcNow.ToString("o")
            });

            // Asset 2: Không gắn với Project nào (Asset cấp Workspace)
            db.Assets.Add(new Asset
            {
                Id = standaloneAssetId,
                WorkspaceId = wsId,
                ProjectId = null,
                StorageRootId = "root-default-images",
                RelativePath = $"{wsId}/common/{standaloneAssetId}.png",
                AssetType = "Image",
                ByteSize = 200,
                Sha256Hash = "hash2",
                Status = "Linked",
                CreatedAtUtc = DateTime.UtcNow.ToString("o")
            });

            await db.SaveChangesAsync();
        }

        // 1. Kiểm tra: Nếu cố xóa Project trực tiếp mà không detach Asset -> SQLite chặn với FK violation (NO ACTION)
        await using (var db = new TiToAIDbContext(dbOptions))
        {
            bool noActionTriggered = false;
            try
            {
                var proj = await db.Projects.FirstAsync(p => p.Id == projId);
                db.Projects.Remove(proj);
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqliteException sqlEx && sqlEx.SqliteErrorCode == 19)
            {
                noActionTriggered = true;
                Console.WriteLine($"    -> Trực tiếp xóa Project bị chặn bởi Asset ON DELETE NO ACTION: PASS ({sqlEx.Message})");
            }
            Assert(noActionTriggered, "Bắt buộc phải chặn xóa Project khi còn Asset tham chiếu qua ON DELETE NO ACTION.");
        }

        // 2. Thực thi Detach an toàn theo chuẩn: UPDATE Asset SET project_id = NULL WHERE workspace_id = @id AND project_id IS NOT NULL;
        await using (var db = new TiToAIDbContext(dbOptions))
        {
            var affected = await db.Assets
                .Where(a => a.WorkspaceId == wsId && a.ProjectId != null)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.ProjectId, (string?)null));

            Assert(affected == 1, $"Phải detach đúng 1 asset có ProjectId, thực tế: {affected}");

            // Xác minh cả 2 Asset đều có ProjectId = null nhưng WorkspaceId vẫn là wsId (bảo toàn quyền sở hữu kênh)
            var asset1 = await db.Assets.FirstAsync(a => a.Id == linkedAssetId);
            var asset2 = await db.Assets.FirstAsync(a => a.Id == standaloneAssetId);

            Assert(asset1.ProjectId == null, "Asset 1 ProjectId phải là null sau detach.");
            Assert(asset1.WorkspaceId == wsId, "Asset 1 WorkspaceId phải được bảo toàn nguyên vẹn.");
            Assert(asset2.ProjectId == null, "Asset 2 ProjectId vẫn là null.");
            Assert(asset2.WorkspaceId == wsId, "Asset 2 WorkspaceId vẫn là wsId.");
        }

        // 3. Xóa Workspace qua WorkspaceService.DeleteAsync -> Phải thành công trơn tru không lỗi FK
        var delRes = await workspaceService.DeleteAsync(wsId);
        Assert(delRes.Deleted, "Workspace phải được xóa thành công sau khi detach.");

        await using (var db = new TiToAIDbContext(dbOptions))
        {
            var wsExists = await db.Workspaces.AnyAsync(w => w.Id == wsId);
            Assert(!wsExists, "Workspace phải bị xóa.");
            var assetsExist = await db.Assets.AnyAsync(a => a.WorkspaceId == wsId);
            Assert(!assetsExist, "Assets của Workspace phải được cascade dọn dẹp sạch sẽ.");
        }

        Console.WriteLine("    -> Detach Asset bảo toàn ON DELETE NO ACTION và WorkspaceId: PASS");
    }

    private static async Task TestConservativeSideEffectReconciliationGuardAsync(
        WorkspaceService workspaceService,
        DbContextOptions<TiToAIDbContext> dbOptions)
    {
        Console.WriteLine("\n[T07a.7] Kiểm tra Guard bảo thủ: Chặn xóa khi có Job SideEffectSubmitted == 1 chưa đối soát...");

        // Helper tạo Workspace + Project + Scene
        async Task<(string WorkspaceId, string ProjectId, string SceneId)> CreateWsAndSceneAsync(string name, string handle)
        {
            var wsRes = await workspaceService.CreateAsync(new WorkspaceCreateRequest(name, handle, "Topic"));
            var pId = Guid.NewGuid().ToString("D").ToLowerInvariant();
            var sId = Guid.NewGuid().ToString("D").ToLowerInvariant();

            await using var db = new TiToAIDbContext(dbOptions);
            db.Projects.Add(new Project
            {
                Id = pId,
                WorkspaceId = wsRes.WorkspaceId,
                Name = "Project",
                CreatedAtUtc = DateTime.UtcNow.ToString("o"),
                UpdatedAtUtc = DateTime.UtcNow.ToString("o")
            });
            db.Scenes.Add(new Scene
            {
                Id = sId,
                ProjectId = pId,
                WorkspaceId = wsRes.WorkspaceId,
                SequenceOrder = 1,
                Name = "Scene",
                TargetDurationSeconds = 5,
                CreatedAtUtc = DateTime.UtcNow.ToString("o"),
                UpdatedAtUtc = DateTime.UtcNow.ToString("o")
            });
            await db.SaveChangesAsync();
            return (wsRes.WorkspaceId, pId, sId);
        }

        // Ca 1: Job State = Succeeded, SideEffectSubmitted = 1, ResultReference = null -> CHẶN
        {
            var (wsId, projId, sceneId) = await CreateWsAndSceneAsync("Kênh Guard Ca 1", "@guard_ca1");
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                db.Jobs.Add(new Job
                {
                    Id = Guid.NewGuid().ToString("D").ToLowerInvariant(),
                    WorkspaceId = wsId,
                    ProjectId = projId,
                    SceneId = sceneId,
                    TaskType = "VideoRender",
                    State = "Succeeded", // Terminal!
                    SideEffectSubmitted = 1, // Đã submit side-effect!
                    ResultReference = null, // Chưa có kết quả đối soát!
                    ProviderKey = "museai",
                    StorageRootId = "root-default-videos",
                    CreatedAtUtc = DateTime.UtcNow.ToString("o"),
                    UpdatedAtUtc = DateTime.UtcNow.ToString("o")
                });
                await db.SaveChangesAsync();
            }

            bool blocked = false;
            try
            {
                await workspaceService.DeleteAsync(wsId);
            }
            catch (WorkspaceConcurrencyConflictException)
            {
                blocked = true;
            }
            Assert(blocked, "Ca 1: Phải chặn xóa khi Job Succeeded + SideEffectSubmitted=1 nhưng ResultReference=null!");
            Console.WriteLine("    -> Ca 1 (Succeeded, SideEffectSubmitted=1, ResultReference=null): BỊ CHẶN (PASS)");
        }

        // Ca 2: Job State = Failed, SideEffectSubmitted = 1, ResultReference = "   " (chuỗi rỗng / whitespace) -> CHẶN
        {
            var (wsId, projId, sceneId) = await CreateWsAndSceneAsync("Kênh Guard Ca 2", "@guard_ca2");
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                db.Jobs.Add(new Job
                {
                    Id = Guid.NewGuid().ToString("D").ToLowerInvariant(),
                    WorkspaceId = wsId,
                    ProjectId = projId,
                    SceneId = sceneId,
                    TaskType = "Image",
                    State = "Failed", // Terminal!
                    SideEffectSubmitted = 1,
                    ResultReference = "   ", // Khoảng trắng / rỗng
                    ProviderKey = "gemini-web",
                    StorageRootId = "root-default-images",
                    CreatedAtUtc = DateTime.UtcNow.ToString("o"),
                    UpdatedAtUtc = DateTime.UtcNow.ToString("o")
                });
                await db.SaveChangesAsync();
            }

            bool blocked = false;
            try
            {
                await workspaceService.DeleteAsync(wsId);
            }
            catch (WorkspaceConcurrencyConflictException)
            {
                blocked = true;
            }
            Assert(blocked, "Ca 2: Phải chặn xóa khi Job Failed + SideEffectSubmitted=1 nhưng ResultReference whitespace!");
            Console.WriteLine("    -> Ca 2 (Failed, SideEffectSubmitted=1, ResultReference whitespace): BỊ CHẶN (PASS)");
        }

        // Ca 3: Job State = Canceled, SideEffectSubmitted = 1, ResultReference = null -> CHẶN
        {
            var (wsId, projId, sceneId) = await CreateWsAndSceneAsync("Kênh Guard Ca 3", "@guard_ca3");
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                db.Jobs.Add(new Job
                {
                    Id = Guid.NewGuid().ToString("D").ToLowerInvariant(),
                    WorkspaceId = wsId,
                    ProjectId = projId,
                    SceneId = sceneId,
                    TaskType = "VideoPrompt",
                    State = "Canceled", // Terminal!
                    SideEffectSubmitted = 1,
                    ResultReference = null,
                    ProviderKey = "chatgpt-web",
                    StorageRootId = "root-default-docs",
                    CreatedAtUtc = DateTime.UtcNow.ToString("o"),
                    UpdatedAtUtc = DateTime.UtcNow.ToString("o")
                });
                await db.SaveChangesAsync();
            }

            bool blocked = false;
            try
            {
                await workspaceService.DeleteAsync(wsId);
            }
            catch (WorkspaceConcurrencyConflictException)
            {
                blocked = true;
            }
            Assert(blocked, "Ca 3: Phải chặn xóa khi Job Canceled + SideEffectSubmitted=1 nhưng ResultReference=null!");
            Console.WriteLine("    -> Ca 3 (Canceled, SideEffectSubmitted=1, ResultReference=null): BỊ CHẶN (PASS)");
        }

        // Ca 4: Job State = AwaitingUser, SideEffectSubmitted = 1, ResultReference = "https://..." -> CHẶN
        {
            var (wsId, projId, sceneId) = await CreateWsAndSceneAsync("Kênh Guard Ca 4", "@guard_ca4");
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                db.Jobs.Add(new Job
                {
                    Id = Guid.NewGuid().ToString("D").ToLowerInvariant(),
                    WorkspaceId = wsId,
                    ProjectId = projId,
                    SceneId = sceneId,
                    TaskType = "Script",
                    State = "AwaitingUser", // State này chưa xong đối soát với user!
                    SideEffectSubmitted = 1,
                    ResultReference = "https://example.com/checkpoints/1",
                    ProviderKey = "chatgpt-web",
                    StorageRootId = "root-default-docs",
                    CreatedAtUtc = DateTime.UtcNow.ToString("o"),
                    UpdatedAtUtc = DateTime.UtcNow.ToString("o")
                });
                await db.SaveChangesAsync();
            }

            bool blocked = false;
            try
            {
                await workspaceService.DeleteAsync(wsId);
            }
            catch (WorkspaceConcurrencyConflictException)
            {
                blocked = true;
            }
            Assert(blocked, "Ca 4: Phải chặn xóa khi Job State=AwaitingUser kể cả khi đã có ResultReference!");
            Console.WriteLine("    -> Ca 4 (AwaitingUser, SideEffectSubmitted=1): BỊ CHẶN (PASS)");
        }

        // Ca 5: Job State = Succeeded, SideEffectSubmitted = 1, ResultReference = "https://valid.ref" -> CHO PHÉP XÓA
        {
            var (wsId, projId, sceneId) = await CreateWsAndSceneAsync("Kênh Guard Ca 5", "@guard_ca5");
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                db.Jobs.Add(new Job
                {
                    Id = Guid.NewGuid().ToString("D").ToLowerInvariant(),
                    WorkspaceId = wsId,
                    ProjectId = projId,
                    SceneId = sceneId,
                    TaskType = "VideoRender",
                    State = "Succeeded",
                    SideEffectSubmitted = 1,
                    ResultReference = "https://provider.api.internal/assets/video_123.mp4", // ĐÃ ĐỐI SOÁT HỢP LỆ
                    ProviderKey = "museai",
                    StorageRootId = "root-default-videos",
                    CreatedAtUtc = DateTime.UtcNow.ToString("o"),
                    UpdatedAtUtc = DateTime.UtcNow.ToString("o")
                });
                await db.SaveChangesAsync();
            }

            var delRes = await workspaceService.DeleteAsync(wsId);
            Assert(delRes.Deleted, "Ca 5: Phải cho phép xóa khi Job Succeeded + SideEffectSubmitted=1 VÀ ResultReference hợp lệ!");
            Console.WriteLine("    -> Ca 5 (Succeeded, SideEffectSubmitted=1, ResultReference hợp lệ): XÓA THÀNH CÔNG (PASS)");
        }

        // Ca 6: Job State = Succeeded, SideEffectSubmitted = 0, ResultReference = null -> CHO PHÉP XÓA
        {
            var (wsId, projId, sceneId) = await CreateWsAndSceneAsync("Kênh Guard Ca 6", "@guard_ca6");
            await using (var db = new TiToAIDbContext(dbOptions))
            {
                db.Jobs.Add(new Job
                {
                    Id = Guid.NewGuid().ToString("D").ToLowerInvariant(),
                    WorkspaceId = wsId,
                    ProjectId = projId,
                    SceneId = sceneId,
                    TaskType = "Script",
                    State = "Succeeded",
                    SideEffectSubmitted = 0, // Không có side-effect ngoài
                    ResultReference = null,
                    ProviderKey = "chatgpt-web",
                    StorageRootId = "root-default-docs",
                    CreatedAtUtc = DateTime.UtcNow.ToString("o"),
                    UpdatedAtUtc = DateTime.UtcNow.ToString("o")
                });
                await db.SaveChangesAsync();
            }

            var delRes = await workspaceService.DeleteAsync(wsId);
            Assert(delRes.Deleted, "Ca 6: Phải cho phép xóa khi Job Succeeded + SideEffectSubmitted=0!");
            Console.WriteLine("    -> Ca 6 (Succeeded, SideEffectSubmitted=0): XÓA THÀNH CÔNG (PASS)");
        }

        Console.WriteLine("    -> Toàn bộ ma trận kiểm tra Guard bảo thủ Side-Effect Reconciliation: PASS");
    }

    private static async Task TestAccountLinkingAndGuardsAsync(
        AccountLinkService accountService,
        WorkspaceService workspaceService,
        TestProfileIdentityResolver fakeResolver)
    {
        Console.WriteLine("\n[T07b.1] Kiểm tra Liên kết Tài khoản (Account Linking & Identity Verification)...");

        var wsRes = await workspaceService.CreateAsync(new WorkspaceCreateRequest("Kênh MuseAI Test", "@musetest", "AI Music"));
        var wsId = wsRes.WorkspaceId;

        // Cấu hình fake resolver
        var validProfileId = "profile_muse_01";
        fakeResolver.SetupResult(validProfileId, "MuseAI", ProfileIdentityResolutionResult.Success(
            validProfileId, "MuseAI", "muse_account_alpha", "Connected"));

        var unverifiedProfileId = "profile_unverified";
        fakeResolver.SetupResult(unverifiedProfileId, "MuseAI", new ProfileIdentityResolutionResult(
            true, unverifiedProfileId, "MuseAI", "some_user", "Unverified", null));

        // 1. Thử link với Profile Unverified -> Chặn
        try
        {
            await accountService.LinkAsync(new AccountLinkRequest(wsId, "MuseAI", unverifiedProfileId));
            throw new InvalidOperationException("Phải chặn liên kết khi SessionStatus là Unverified!");
        }
        catch (AccountValidationException)
        {
            Console.WriteLine("    -> Chặn liên kết tài khoản Unverified: PASS");
        }

        // 2. Thử link với Provider không hỗ trợ
        try
        {
            await accountService.LinkAsync(new AccountLinkRequest(wsId, "Facebook", validProfileId));
            throw new InvalidOperationException("Phải chặn provider lạ!");
        }
        catch (AccountValidationException)
        {
            Console.WriteLine("    -> Chặn provider không hỗ trợ ('Facebook'): PASS");
        }

        // 3. Liên kết hợp lệ lần đầu
        var linkRes = await accountService.LinkAsync(new AccountLinkRequest(wsId, "MuseAI", validProfileId));
        Assert(linkRes.Provider == "MuseAI", "Provider phải là MuseAI");
        Assert(linkRes.AccountIdentity == "muse_account_alpha", "AccountIdentity không khớp");
        Assert(linkRes.Status == "Connected", "Status phải là Connected");
        Console.WriteLine($"    -> Liên kết MuseAI thành công: AccountId = {linkRes.AccountId}");

        // 4. Cố liên kết tài khoản MuseAI thứ 2 cho cùng Workspace -> Phải chặn
        var secondProfileId = "profile_muse_02";
        fakeResolver.SetupResult(secondProfileId, "MuseAI", ProfileIdentityResolutionResult.Success(
            secondProfileId, "MuseAI", "muse_account_beta", "Connected"));

        try
        {
            await accountService.LinkAsync(new AccountLinkRequest(wsId, "MuseAI", secondProfileId));
            throw new InvalidOperationException("Mỗi Workspace chỉ có tối đa 1 liên kết MuseAI Active!");
        }
        catch (AccountConcurrencyConflictException)
        {
            Console.WriteLine("    -> Chặn liên kết MuseAI thứ hai cho cùng Workspace: PASS");
        }

        // 5. Kiểm tra ListByWorkspace
        var accounts = await accountService.ListByWorkspaceAsync(wsId);
        Assert(accounts.Count == 1, "Chỉ được có 1 tài khoản active");
        Assert(accounts[0].AccountId == linkRes.AccountId, "AccountId trong list không khớp");
        Console.WriteLine("    -> ListByWorkspaceAsync hiển thị đúng danh sách liên kết: PASS");
    }

    private static async Task TestCrossWorkspaceSecurityAndRelinkAsync(
        AccountLinkService accountService,
        WorkspaceService workspaceService,
        TestProfileIdentityResolver fakeResolver)
    {
        Console.WriteLine("\n[T07b.2] Kiểm tra Cross-workspace Guard & Unlink/Relink...");

        var ws1 = (await workspaceService.CreateAsync(new WorkspaceCreateRequest("Workspace 1", "@ws1", "Topic 1"))).WorkspaceId;
        var ws2 = (await workspaceService.CreateAsync(new WorkspaceCreateRequest("Workspace 2", "@ws2", "Topic 2"))).WorkspaceId;

        var profileId = "profile_tiktok_shared";
        fakeResolver.SetupResult(profileId, "TikTok", ProfileIdentityResolutionResult.Success(
            profileId, "TikTok", "tiktok_user_shared", "Connected"));

        // Link TikTok vào Workspace 1
        var acc1 = await accountService.LinkAsync(new AccountLinkRequest(ws1, "TikTok", profileId));
        Console.WriteLine($"    -> Đã liên kết TikTok vào Workspace 1: ID = {acc1.AccountId}");

        // Workspace 2 cố link cùng tài khoản TikTok đó -> Chặn vì tài khoản đã thuộc Workspace 1
        try
        {
            await accountService.LinkAsync(new AccountLinkRequest(ws2, "TikTok", profileId));
            throw new InvalidOperationException("Không được cho phép tài khoản gắn vào Workspace khác khi đang active!");
        }
        catch (AccountConcurrencyConflictException)
        {
            Console.WriteLine("    -> Chặn gắn cùng tài khoản TikTok vào Workspace khác: PASS");
        }

        // Workspace 2 cố Hủy liên kết tài khoản của Workspace 1 -> Chặn với UNAUTHORIZED_WORKSPACE
        try
        {
            await accountService.UnlinkAsync(new AccountUnlinkRequest(ws2, acc1.AccountId));
            throw new InvalidOperationException("Workspace 2 không được phép thao tác trên account của Workspace 1!");
        }
        catch (UnauthorizedWorkspaceException)
        {
            Console.WriteLine("    -> Cross-workspace Guard chặn unlink trái phép: PASS");
        }

        // Workspace 1 Hủy liên kết tài khoản hợp lệ
        var unlinkRes = await accountService.UnlinkAsync(new AccountUnlinkRequest(ws1, acc1.AccountId));
        Assert(unlinkRes.Status == "Unlinked", "Trạng thái sau unlink phải là Unlinked");
        Console.WriteLine("    -> Hủy liên kết thành công (status = Unlinked): PASS");

        // Kiểm tra sau khi Unlink, ListByWorkspace không còn trả về tài khoản đã unlink
        var activeAccountsWs1 = await accountService.ListByWorkspaceAsync(ws1);
        Assert(activeAccountsWs1.Count == 0, "Không còn tài khoản active sau khi unlink");
        Console.WriteLine("    -> ListByWorkspace loại bỏ tài khoản đã Unlinked: PASS");

        // Relink: Bây giờ Workspace 2 (hoặc chính Workspace 1) có thể liên kết tài khoản này
        var relinkRes = await accountService.LinkAsync(new AccountLinkRequest(ws2, "TikTok", profileId));
        Assert(relinkRes.AccountId != acc1.AccountId, "Bản ghi relink mới phải có AccountId mới");
        Assert(relinkRes.Status == "Connected", "Trạng thái relink phải là Connected");
        Console.WriteLine($"    -> Relink sang Workspace 2 thành công nhờ partial index: ID = {relinkRes.AccountId}");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Khẳng định thất bại (Assertion Failed): {message}");
        }
    }
}
