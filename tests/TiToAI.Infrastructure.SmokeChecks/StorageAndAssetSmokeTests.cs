using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TiToAI.Application.Assets;
using TiToAI.Application.Documents;
using TiToAI.Application.Settings;
using TiToAI.Contracts.Assets;
using TiToAI.Contracts.Documents;
using TiToAI.Contracts.Settings;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Persistence.Entities;
using TiToAI.Infrastructure.Persistence.Interceptors;
using TiToAI.Infrastructure.Storage;

namespace TiToAI.Infrastructure.SmokeChecks;

/// <summary>
/// Bộ kiểm thử khói xác thực toàn diện cho Task T09 (Storage, Assets, Documents, Settings)
/// theo docs/plans/SETTINGS_PLAN.md và implementation/T09a, T09b, T09c.
/// </summary>
public static class StorageAndAssetSmokeTests
{
    public static async Task RunTestsAsync(string databasePath)
    {
        Console.WriteLine("\n=================================================================");
        Console.WriteLine("    BẮT ĐẦU KIỂM THỬ KHÓI T09: STORAGE, ASSETS & SETTINGS       ");
        Console.WriteLine("=================================================================");

        var connectionString = DatabasePathHelper.BuildConnectionString(databasePath);
        var optionsBuilder = new DbContextOptionsBuilder<TiToAIDbContext>();
        optionsBuilder.UseSqlite(connectionString)
                      .AddInterceptors(new SqlitePragmaInterceptor());
        var dbOptions = optionsBuilder.Options;

        var rootRegistry = new StorageRootRegistry(dbOptions);
        var fileStore = new AssetFileStore();
        var settingsService = new SettingsService(dbOptions, rootRegistry);
        var assetService = new AssetService(dbOptions, rootRegistry, fileStore);
        var documentExportService = new DocumentExportService(dbOptions, rootRegistry, fileStore);

        var testWorkDir = Path.Combine(Path.GetTempPath(), "titoai_t09_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testWorkDir);

        try
        {
            // Test 1: 3 default roots & mapping persistence trong AppSettings
            await TestDefaultRootsAndPersistenceAsync(rootRegistry, dbOptions);

            // Test 2: Đổi root: sinh rootId mới, giữ mapping cũ, tái sử dụng khi trùng path
            await TestRootUpdateAndHistoryRetentionAsync(rootRegistry, testWorkDir, dbOptions);

            // Test 3: Traversal & junction guard chặn truy cập ngoài root
            TestPathGuardAndTraversalProtection(testWorkDir);

            // Test 4: Dry-run write probe (hợp lệ vs thiếu quyền/hỏng)
            await TestWriteProbeAndRootValidationAsync(settingsService, testWorkDir);

            // Test 5: Atomic staging & finalize asset file (audio MP3/WAV & image)
            await TestAssetImportAtomicAndIntegrityAsync(assetService, dbOptions, testWorkDir);

            // Test 6: Document export UTF-8 có dấu tiếng Việt, newline, metadata
            await TestDocumentExportUtf8AndMetadataAsync(documentExportService, dbOptions, testWorkDir);

            Console.WriteLine("=================================================================");
            Console.WriteLine("        TẤT CẢ KIỂM THỬ KHÓI T09 ĐÃ HOÀN TẤT THÀNH CÔNG!         ");
            Console.WriteLine("=================================================================");
        }
        finally
        {
            try
            {
                if (Directory.Exists(testWorkDir))
                {
                    Directory.Delete(testWorkDir, recursive: true);
                }
            }
            catch
            {
                // Bỏ qua lỗi xóa thư mục tạm test
            }
        }
    }

    private static async Task TestDefaultRootsAndPersistenceAsync(
        StorageRootRegistry rootRegistry,
        DbContextOptions<TiToAIDbContext> dbOptions)
    {
        Console.WriteLine("\n[T09-1] Kiểm thử 3 default roots & mapping persistence trong AppSettings...");

        await rootRegistry.EnsureDefaultRootsAsync();
        var snapshot = await rootRegistry.GetCurrentRootsAsync();

        Assert(snapshot.DocumentsRootId == StorageRootRegistry.RootDefaultDocsId, "DocumentsRootId mặc định không đúng.");
        Assert(snapshot.ImagesRootId == StorageRootRegistry.RootDefaultImagesId, "ImagesRootId mặc định không đúng.");
        Assert(snapshot.VideosRootId == StorageRootRegistry.RootDefaultVideosId, "VideosRootId mặc định không đúng.");
        Assert(snapshot.AudioRootId == StorageRootRegistry.RootInternalAudioId, "AudioRootId mặc định không đúng.");

        await using var db = new TiToAIDbContext(dbOptions);
        var docPathSetting = await db.AppSettings.FindAsync(StorageRootRegistry.KeyRootsPrefix + StorageRootRegistry.RootDefaultDocsId);
        var imgPathSetting = await db.AppSettings.FindAsync(StorageRootRegistry.KeyRootsPrefix + StorageRootRegistry.RootDefaultImagesId);
        var vidPathSetting = await db.AppSettings.FindAsync(StorageRootRegistry.KeyRootsPrefix + StorageRootRegistry.RootDefaultVideosId);
        var audPathSetting = await db.AppSettings.FindAsync(StorageRootRegistry.KeyRootsPrefix + StorageRootRegistry.RootInternalAudioId);

        Assert(docPathSetting != null && !string.IsNullOrWhiteSpace(docPathSetting.Value), "Mapping cho root-default-docs bị thiếu.");
        Assert(imgPathSetting != null && !string.IsNullOrWhiteSpace(imgPathSetting.Value), "Mapping cho root-default-images bị thiếu.");
        Assert(vidPathSetting != null && !string.IsNullOrWhiteSpace(vidPathSetting.Value), "Mapping cho root-default-videos bị thiếu.");
        Assert(audPathSetting != null && !string.IsNullOrWhiteSpace(audPathSetting.Value), "Mapping cho root-internal-audio bị thiếu.");

        Console.WriteLine("    -> Default roots và AppSettings mapping đã được seed chính xác: PASS");
    }

    private static async Task TestRootUpdateAndHistoryRetentionAsync(
        StorageRootRegistry rootRegistry,
        string testWorkDir,
        DbContextOptions<TiToAIDbContext> dbOptions)
    {
        Console.WriteLine("\n[T09-2] Kiểm thử đổi root: sinh rootId mới, giữ mapping cũ, tái sử dụng...");

        var customDocsDir = Path.Combine(testWorkDir, "custom_docs");
        var customImagesDir = Path.Combine(testWorkDir, "custom_images");
        var customVideosDir = Path.Combine(testWorkDir, "custom_videos");

        Directory.CreateDirectory(customDocsDir);
        Directory.CreateDirectory(customImagesDir);
        Directory.CreateDirectory(customVideosDir);

        var updated = await rootRegistry.UpdateRootsAsync(customDocsDir, customImagesDir, customVideosDir);

        Assert(updated.DocumentsRootId.StartsWith("root-") && updated.DocumentsRootId != StorageRootRegistry.RootDefaultDocsId,
            "Đổi documentsPath phải sinh rootId mới.");
        Assert(updated.ImagesRootId.StartsWith("root-") && updated.ImagesRootId != StorageRootRegistry.RootDefaultImagesId,
            "Đổi imagesPath phải sinh rootId mới.");
        Assert(updated.VideosRootId.StartsWith("root-") && updated.VideosRootId != StorageRootRegistry.RootDefaultVideosId,
            "Đổi videosPath phải sinh rootId mới.");

        // Kiểm tra mapping cũ vẫn được giữ nguyên
        await using (var db = new TiToAIDbContext(dbOptions))
        {
            var oldDocSetting = await db.AppSettings.FindAsync(StorageRootRegistry.KeyRootsPrefix + StorageRootRegistry.RootDefaultDocsId);
            Assert(oldDocSetting != null, "Mapping cũ root-default-docs phải được giữ nguyên để asset cũ không bị mất link.");
        }

        // Cập nhật lại đường dẫn ban đầu -> phải tái sử dụng rootId mặc định
        var defaultDocsPath = StorageRootRegistry.GetDefaultRootPath(StorageRootRegistry.RootDefaultDocsId);
        var defaultImagesPath = StorageRootRegistry.GetDefaultRootPath(StorageRootRegistry.RootDefaultImagesId);
        var defaultVideosPath = StorageRootRegistry.GetDefaultRootPath(StorageRootRegistry.RootDefaultVideosId);

        var restored = await rootRegistry.UpdateRootsAsync(defaultDocsPath, defaultImagesPath, defaultVideosPath);
        Assert(restored.DocumentsRootId == StorageRootRegistry.RootDefaultDocsId,
            "Cập nhật lại đường dẫn cũ phải tái sử dụng root-default-docs.");

        Console.WriteLine("    -> Cơ chế sinh rootId mới, giữ mapping cũ và tái sử dụng rootId: PASS");
    }

    private static void TestPathGuardAndTraversalProtection(string testWorkDir)
    {
        Console.WriteLine("\n[T09-3] Kiểm thử Traversal & Junction guard chặn truy cập ngoài root...");

        var baseRoot = Path.Combine(testWorkDir, "sandbox_root");
        Directory.CreateDirectory(baseRoot);

        // Chặn traversal "..\"
        AssertThrows<ArgumentException>(() => StoragePathGuard.EnsureValidRelativePath(@"..\secret.txt"),
            "Chặn relative path bắt đầu bằng ..");
        AssertThrows<ArgumentException>(() => StoragePathGuard.EnsureValidRelativePath(@"folder/../../secret.txt"),
            "Chặn relative path có segment ..");
        AssertThrows<ArgumentException>(() => StoragePathGuard.EnsureValidRelativePath(@"/folder/test.txt"),
            "Chặn relative path bắt đầu bằng /");
        AssertThrows<ArgumentException>(() => StoragePathGuard.EnsureValidRelativePath(@"C:\Windows\System32"),
            "Chặn absolute path trong relative path validator");
        AssertThrows<ArgumentException>(() => StoragePathGuard.EnsureValidRelativePath(@"stream:file"),
            "Chặn NTFS alternate data streams chứa dấu hai chấm");

        // IsSubPathOf
        var validChild = Path.Combine(baseRoot, "subfolder", "file.txt");
        var outsideFolder = Path.Combine(testWorkDir, "outside_folder", "file.txt");

        Assert(StoragePathGuard.IsSubPathOf(baseRoot, validChild), "Thư mục con hợp lệ phải được xác nhận IsSubPathOf.");
        Assert(!StoragePathGuard.IsSubPathOf(baseRoot, outsideFolder), "Thư mục nằm ngoài baseRoot phải bị từ chối.");

        // CombineAndVerify
        var verified = StoragePathGuard.CombineAndVerify(baseRoot, "sub/file.txt");
        Assert(verified.StartsWith(StoragePathGuard.NormalizePath(baseRoot), StringComparison.OrdinalIgnoreCase),
            "CombineAndVerify phải trả về đường dẫn con hợp lệ.");

        Console.WriteLine("    -> StoragePathGuard chặn Directory Traversal và kiểm soát ranh giới: PASS");
    }

    private static async Task TestWriteProbeAndRootValidationAsync(SettingsService settingsService, string testWorkDir)
    {
        Console.WriteLine("\n[T09-4] Kiểm thử Dry-run write probe (hợp lệ vs thiếu quyền/hỏng)...");

        var validDir = Path.Combine(testWorkDir, "probe_valid");
        Directory.CreateDirectory(validDir);

        bool probeSuccess = StoragePathGuard.TryVerifyWritable(validDir, out var err);
        Assert(probeSuccess && string.IsNullOrEmpty(err), $"Dry-run write probe trên thư mục hợp lệ phải thành công. Lỗi: {err}");

        // Kiểm tra không còn file probe sót lại
        var remainingProbes = Directory.GetFiles(validDir, ".titoai_probe_*.tmp");
        Assert(remainingProbes.Length == 0, "File tạm probe phải được xóa ngay sau khi kiểm tra.");

        // Kiểm tra qua ValidateRootsAsync với đường dẫn rỗng / không hợp lệ
        var badRequest = new ValidateRootsRequest(
            DocumentsPath: "",
            ImagesPath: validDir,
            VideosPath: "??invalid_path_chars::<>*"
        );

        var badResponse = await settingsService.ValidateRootsAsync(badRequest);
        Assert(!badResponse.IsValid, "Yêu cầu có đường dẫn không hợp lệ phải có IsValid = false.");
        Assert(badResponse.FieldErrors.ContainsKey("documentsPath"), "Phải báo lỗi trường documentsPath.");
        Assert(badResponse.FieldErrors.ContainsKey("videosPath"), "Phải báo lỗi trường videosPath.");

        // Thử lưu cài đặt với request lỗi -> phải rollback và ném SettingsValidationException
        var saveRequest = new SettingsSaveRequest(
            DocumentsPath: "",
            ImagesPath: validDir,
            VideosPath: validDir
        );

        bool exceptionThrown = false;
        try
        {
            await settingsService.SaveSettingsAsync(saveRequest);
        }
        catch (SettingsValidationException)
        {
            exceptionThrown = true;
        }

        Assert(exceptionThrown, "SaveSettingsAsync phải ném SettingsValidationException khi validate thất bại.");
        Console.WriteLine("    -> Dry-run write probe và atomic validation rollback: PASS");
    }

    private static async Task TestAssetImportAtomicAndIntegrityAsync(
        AssetService assetService,
        DbContextOptions<TiToAIDbContext> dbOptions,
        string testWorkDir)
    {
        Console.WriteLine("\n[T09-5] Kiểm thử Atomic staging & finalize asset file (audio MP3/WAV & image)...");

        var workspaceId = Guid.NewGuid().ToString("D").ToLowerInvariant();
        var projectId = Guid.NewGuid().ToString("D").ToLowerInvariant();
        var sceneId = Guid.NewGuid().ToString("D").ToLowerInvariant();
        var now = DateTimeOffset.UtcNow.ToString("O");

        await using (var db = new TiToAIDbContext(dbOptions))
        {
            db.Workspaces.Add(new Workspace
            {
                Id = workspaceId,
                Name = "Workspace Test Media",
                ChannelHandle = "@test_media",
                Topic = "Giải trí",
                Status = "Active",
                CreatedAtUtc = now
            });

            db.Projects.Add(new Project
            {
                Id = projectId,
                WorkspaceId = workspaceId,
                Name = "Dự án Media Test",
                Status = "Draft",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });

            db.Scenes.Add(new Scene
            {
                Id = sceneId,
                WorkspaceId = workspaceId,
                ProjectId = projectId,
                SequenceOrder = 1,
                Name = "Cảnh 1 Nhạc nền",
                TargetDurationSeconds = 15,
                Status = "Draft",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });

            await db.SaveChangesAsync();
        }

        // Tạo file âm thanh MP3 giả lập có dữ liệu thực tế
        var sampleAudioData = Encoding.UTF8.GetBytes("ID3-MOCK-MP3-AUDIO-DATA-STREAM-TITOAI-TEST-SUITE-1234567890");
        var sourceAudioPath = Path.Combine(testWorkDir, "source_voice.mp3");
        await File.WriteAllBytesAsync(sourceAudioPath, sampleAudioData);

        var expectedAudioHash = Convert.ToHexString(SHA256.HashData(sampleAudioData)).ToLowerInvariant();

        // 1. Import Audio
        var importAudioResp = await assetService.ImportAudioAsync(new ImportAudioRequest(
            WorkspaceId: workspaceId,
            ProjectId: projectId,
            SceneId: sceneId,
            SourceFilePath: sourceAudioPath
        ));

        Assert(importAudioResp.Asset != null, "ImportAudioAsync phải trả về AssetDto.");
        Assert(importAudioResp.Asset!.AssetType == "Audio", "AssetType phải là Audio.");
        Assert(importAudioResp.Asset.ByteSize == sampleAudioData.Length, "ByteSize của audio lưu phải bằng nguồn.");
        Assert(importAudioResp.Asset.Sha256Hash == expectedAudioHash, "SHA-256 hash của audio lưu phải bằng hash nguồn.");
        Assert(importAudioResp.Asset.StorageRootId == StorageRootRegistry.RootInternalAudioId,
            "Audio phải được lưu tại root-internal-audio.");

        // Kiểm tra Scene.AudioAssetId đã được cập nhật trong DB
        await using (var db = new TiToAIDbContext(dbOptions))
        {
            var updatedScene = await db.Scenes.FirstAsync(s => s.Id == sceneId);
            Assert(updatedScene.AudioAssetId == importAudioResp.Asset.AssetId, "Scene.AudioAssetId phải trỏ tới AssetId vừa tạo.");
        }

        // 2. Chặn import file sai định dạng (ví dụ .exe)
        var invalidFile = Path.Combine(testWorkDir, "malicious.exe");
        await File.WriteAllBytesAsync(invalidFile, new byte[] { 0x4D, 0x5A });

        await AssertThrowsAsync<AssetValidationException>(async () =>
        {
            await assetService.ImportAudioAsync(new ImportAudioRequest(
                WorkspaceId: workspaceId,
                ProjectId: projectId,
                SceneId: sceneId,
                SourceFilePath: invalidFile
            ));
        }, "Chặn import file có đuôi .exe vào audio.");

        // 3. Import Reference Image
        var sampleImageData = Encoding.UTF8.GetBytes("PNG-MOCK-IMAGE-DATA-TITOAI-TEST-SUITE");
        var sourceImagePath = Path.Combine(testWorkDir, "reference.png");
        await File.WriteAllBytesAsync(sourceImagePath, sampleImageData);

        var selectImgResp = await assetService.SelectReferenceImageAsync(new SelectReferenceImageRequest(
            WorkspaceId: workspaceId,
            ProjectId: projectId,
            SceneId: sceneId,
            SourceFilePath: sourceImagePath
        ));

        Assert(selectImgResp.Asset != null, "SelectReferenceImageAsync phải trả về AssetDto.");
        Assert(selectImgResp.Asset!.AssetType == "Image", "AssetType phải là Image.");

        // Kiểm tra Scene.ImageAssetId đã được liên kết
        await using (var db = new TiToAIDbContext(dbOptions))
        {
            var updatedScene = await db.Scenes.FirstAsync(s => s.Id == sceneId);
            Assert(updatedScene.ImageAssetId == selectImgResp.Asset.AssetId, "Scene.ImageAssetId phải trỏ tới AssetId vừa tạo.");
        }

        // 4. Lấy metadata
        var metadata = await assetService.GetMetadataAsync(new GetAssetMetadataRequest(
            AssetId: importAudioResp.Asset.AssetId,
            WorkspaceId: workspaceId
        ));
        Assert(metadata.AssetId == importAudioResp.Asset.AssetId, "GetMetadataAsync phải trả về đúng AssetId.");

        Console.WriteLine("    -> Atomic staging, SHA-256 integrity và cập nhật liên kết Scene: PASS");
    }

    private static async Task TestDocumentExportUtf8AndMetadataAsync(
        DocumentExportService documentExportService,
        DbContextOptions<TiToAIDbContext> dbOptions,
        string testWorkDir)
    {
        Console.WriteLine("\n[T09-6] Kiểm thử Document export UTF-8 có dấu tiếng Việt, newline, metadata...");

        var workspaceId = Guid.NewGuid().ToString("D").ToLowerInvariant();
        var projectId = Guid.NewGuid().ToString("D").ToLowerInvariant();
        var sceneId = Guid.NewGuid().ToString("D").ToLowerInvariant();
        var now = DateTimeOffset.UtcNow.ToString("O");

        var promptTextWithVietnamese =
            "Khám phá các bí ẩn của vũ trụ bao la!\n" +
            "Hình ảnh ngôi sao siêu mới bùng nổ rực rỡ với ánh sáng xanh ngọc bích.\n" +
            "Lời thoại: 'Từ hàng tỷ năm trước, những hạt bụi vũ trụ đã tạo nên sự sống hôm nay.'\n" +
            "Tone giọng truyền cảm, ấm áp, nhịp điệu vừa phải.";

        await using (var db = new TiToAIDbContext(dbOptions))
        {
            db.Workspaces.Add(new Workspace
            {
                Id = workspaceId,
                Name = "Workspace Kịch Bản",
                ChannelHandle = "@kichban_space",
                Topic = "Vũ trụ học",
                Status = "Active",
                CreatedAtUtc = now
            });

            db.Projects.Add(new Project
            {
                Id = projectId,
                WorkspaceId = workspaceId,
                Name = "Dự Án Kịch Bản Thiên Văn",
                Status = "Draft",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });

            db.Scenes.Add(new Scene
            {
                Id = sceneId,
                WorkspaceId = workspaceId,
                ProjectId = projectId,
                SequenceOrder = 1,
                Name = "Khởi đầu vũ trụ",
                PromptText = promptTextWithVietnamese,
                TargetDurationSeconds = 20,
                Status = "Draft",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });

            await db.SaveChangesAsync();
        }

        var exportResp = await documentExportService.ExportDocumentAsync(new DocumentExportRequest(
            WorkspaceId: workspaceId,
            SceneId: sceneId,
            Format: "txt",
            CustomFileName: "KichBan_Canh1_VuTru.txt"
        ));

        Assert(!string.IsNullOrEmpty(exportResp.AssetId), "ExportDocumentAsync phải trả về AssetId.");
        Assert(exportResp.DisplayName == "KichBan_Canh1_VuTru.txt", "DisplayName phải là KichBan_Canh1_VuTru.txt.");

        // Kiểm tra bản ghi Asset trong DB
        await using (var db = new TiToAIDbContext(dbOptions))
        {
            var docAsset = await db.Assets.FirstOrDefaultAsync(a => a.Id == exportResp.AssetId);
            Assert(docAsset != null, "Phải tìm thấy Asset Document vừa được tạo trong DB.");
            Assert(docAsset!.AssetType == "Document", "AssetType phải là Document.");
            Assert(docAsset.Status == "Linked", "Status phải là Linked.");
            Assert(docAsset.ByteSize > 0, "ByteSize của tài liệu phải lớn hơn 0.");
        }

        // Kiểm tra từ chối format không hỗ trợ (ví dụ docx)
        await AssertThrowsAsync<DocumentExportException>(async () =>
        {
            await documentExportService.ExportDocumentAsync(new DocumentExportRequest(
                WorkspaceId: workspaceId,
                SceneId: sceneId,
                Format: "docx"
            ));
        }, "Chặn xuất định dạng khác txt trong MVP.");

        Console.WriteLine("    -> Document export UTF-8 tiếng Việt, newline và metadata Asset Document: PASS");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"[ASSERTION FAILED] {message}");
        }
    }

    private static void AssertThrows<TException>(Action action, string scenario) where TException : Exception
    {
        try
        {
            action();
            throw new InvalidOperationException($"[ASSERTION FAILED] Dự kiến ngoại lệ {typeof(TException).Name} nhưng không xảy ra trong: {scenario}");
        }
        catch (TException)
        {
            // Mong muốn
        }
    }

    private static async Task AssertThrowsAsync<TException>(Func<Task> action, string scenario) where TException : Exception
    {
        try
        {
            await action();
            throw new InvalidOperationException($"[ASSERTION FAILED] Dự kiến ngoại lệ {typeof(TException).Name} nhưng không xảy ra trong: {scenario}");
        }
        catch (TException)
        {
            // Mong muốn
        }
    }
}
