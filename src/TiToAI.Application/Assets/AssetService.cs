using System.Diagnostics;
using System.IO;
using Microsoft.EntityFrameworkCore;
using TiToAI.Application.Projects;
using TiToAI.Application.Scenes;
using TiToAI.Application.Workspaces;
using TiToAI.Contracts.Assets;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Persistence.Entities;
using TiToAI.Infrastructure.Storage;

namespace TiToAI.Application.Assets;

public class AssetValidationException : Exception
{
    public AssetValidationException(string message) : base(message) { }
    public AssetValidationException(string message, Exception innerException) : base(message, innerException) { }
}

public class AssetNotFoundException : Exception
{
    public AssetNotFoundException(string assetId)
        : base($"Không tìm thấy tệp tài nguyên (Asset) với mã '{assetId}'.") { }
}

/// <summary>
/// Dịch vụ quản lý tài nguyên media (Audio MP3/WAV, Ảnh tham chiếu, Video)
/// theo docs/plans/implementation/T09b-media-assets.md và SETTINGS_PLAN.md §2–3.
/// </summary>
public sealed class AssetService
{
    private static readonly HashSet<string> AllowedAudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3",
        ".wav"
    };

    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".webp"
    };

    private readonly DbContextOptions<TiToAIDbContext> _dbOptions;
    private readonly StorageRootRegistry _rootRegistry;
    private readonly AssetFileStore _fileStore;

    public AssetService(
        DbContextOptions<TiToAIDbContext> dbOptions,
        StorageRootRegistry rootRegistry,
        AssetFileStore fileStore)
    {
        _dbOptions = dbOptions ?? throw new ArgumentNullException(nameof(dbOptions));
        _rootRegistry = rootRegistry ?? throw new ArgumentNullException(nameof(rootRegistry));
        _fileStore = fileStore ?? throw new ArgumentNullException(nameof(fileStore));
    }

    /// <summary>
    /// Nhập tệp âm thanh (MP3 hoặc WAV) vào thư mục Audio cố định (root-internal-audio),
    /// tính toán SHA-256 và kích thước tệp, lưu Asset và liên kết với Scene.
    /// </summary>
    public async Task<ImportAudioResponse> ImportAudioAsync(
        ImportAudioRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var workspaceId = NormalizeGuid(request.WorkspaceId, nameof(request.WorkspaceId));
        var projectId = NormalizeGuid(request.ProjectId, nameof(request.ProjectId));
        var sceneId = NormalizeGuid(request.SceneId, nameof(request.SceneId));

        if (string.IsNullOrWhiteSpace(request.SourceFilePath) || !File.Exists(request.SourceFilePath))
        {
            throw new AssetValidationException($"Tệp âm thanh nguồn không tồn tại: '{request.SourceFilePath}'.");
        }

        var ext = Path.GetExtension(request.SourceFilePath).ToLowerInvariant();
        if (!AllowedAudioExtensions.Contains(ext))
        {
            throw new AssetValidationException($"Chỉ hỗ trợ tệp âm thanh định dạng .mp3 hoặc .wav. Định dạng hiện tại: '{ext}'.");
        }

        // Kiểm tra định dạng nhị phân thực tế và kích thước tệp
        ValidateAudioFile(request.SourceFilePath, ext);

        await using var dbContext = new TiToAIDbContext(_dbOptions);

        // Kiểm tra quyền sở hữu Workspace, Project và Scene
        await VerifySceneOwnershipAsync(dbContext, workspaceId, projectId, sceneId, cancellationToken).ConfigureAwait(false);

        var audioRootId = StorageRootRegistry.RootInternalAudioId;
        var audioRootPath = await _rootRegistry.ResolveRootPathAsync(audioRootId, cancellationToken).ConfigureAwait(false);

        var assetId = Guid.NewGuid().ToString("N");
        var relativePath = $"{workspaceId}/projects/{projectId}/audio/{assetId}{ext}";

        // Ghi tệp vật lý qua staging & atomic move
        var saveResult = await _fileStore.SaveFileFromPathAsync(
            audioRootPath,
            relativePath,
            request.SourceFilePath,
            cancellationToken
        ).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow.ToString("O");
        var mimeType = ext == ".mp3" ? "audio/mpeg" : "audio/wav";

        var asset = new Asset
        {
            Id = assetId,
            WorkspaceId = workspaceId,
            ProjectId = projectId,
            AssetType = "Audio",
            StorageRootId = audioRootId,
            RelativePath = relativePath,
            ByteSize = saveResult.ByteSize,
            Sha256Hash = saveResult.Sha256Hash,
            Status = "Linked",
            MimeType = mimeType,
            CreatedAtUtc = now
        };

        dbContext.Assets.Add(asset);

        // Cập nhật Scene.AudioAssetId
        var scene = await dbContext.Scenes.FirstAsync(s => s.Id == sceneId, cancellationToken).ConfigureAwait(false);
        scene.AudioAssetId = assetId;
        scene.UpdatedAtUtc = now;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new ImportAudioResponse(ToDto(asset));
    }

    /// <summary>
    /// Nhập ảnh tham chiếu (Reference Image) vào thư mục Ảnh AI hiện hành,
    /// lưu Asset và liên kết với Scene.ImageAssetId.
    /// </summary>
    public async Task<SelectReferenceImageResponse> SelectReferenceImageAsync(
        SelectReferenceImageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var workspaceId = NormalizeGuid(request.WorkspaceId, nameof(request.WorkspaceId));
        var projectId = NormalizeGuid(request.ProjectId, nameof(request.ProjectId));
        var sceneId = NormalizeGuid(request.SceneId, nameof(request.SceneId));

        if (string.IsNullOrWhiteSpace(request.SourceFilePath) || !File.Exists(request.SourceFilePath))
        {
            throw new AssetValidationException($"Tệp ảnh nguồn không tồn tại: '{request.SourceFilePath}'.");
        }

        var ext = Path.GetExtension(request.SourceFilePath).ToLowerInvariant();
        if (!AllowedImageExtensions.Contains(ext))
        {
            throw new AssetValidationException($"Chỉ hỗ trợ tệp ảnh định dạng .png, .jpg, .jpeg, .webp. Định dạng hiện tại: '{ext}'.");
        }

        await using var dbContext = new TiToAIDbContext(_dbOptions);

        await VerifySceneOwnershipAsync(dbContext, workspaceId, projectId, sceneId, cancellationToken).ConfigureAwait(false);

        var currentRoots = await _rootRegistry.GetCurrentRootsAsync(cancellationToken).ConfigureAwait(false);
        var imagesRootId = currentRoots.ImagesRootId;
        var imagesRootPath = currentRoots.ImagesPath;

        var assetId = Guid.NewGuid().ToString("N");
        var relativePath = $"{workspaceId}/projects/{projectId}/images/{assetId}{ext}";

        var saveResult = await _fileStore.SaveFileFromPathAsync(
            imagesRootPath,
            relativePath,
            request.SourceFilePath,
            cancellationToken
        ).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow.ToString("O");
        var mimeType = ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };

        var asset = new Asset
        {
            Id = assetId,
            WorkspaceId = workspaceId,
            ProjectId = projectId,
            AssetType = "Image",
            StorageRootId = imagesRootId,
            RelativePath = relativePath,
            ByteSize = saveResult.ByteSize,
            Sha256Hash = saveResult.Sha256Hash,
            Status = "Linked",
            MimeType = mimeType,
            CreatedAtUtc = now
        };

        dbContext.Assets.Add(asset);

        var scene = await dbContext.Scenes.FirstAsync(s => s.Id == sceneId, cancellationToken).ConfigureAwait(false);
        scene.ImageAssetId = assetId;
        scene.UpdatedAtUtc = now;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new SelectReferenceImageResponse(ToDto(asset));
    }

    /// <summary>
    /// Lấy metadata chi tiết của một Asset theo AssetId và WorkspaceId.
    /// </summary>
    public async Task<AssetDto> GetMetadataAsync(
        GetAssetMetadataRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var workspaceId = NormalizeGuid(request.WorkspaceId, nameof(request.WorkspaceId));
        if (string.IsNullOrWhiteSpace(request.AssetId))
        {
            throw new ArgumentException("Mã assetId không được để trống.", nameof(request.AssetId));
        }

        await using var dbContext = new TiToAIDbContext(_dbOptions);

        var asset = await dbContext.Assets
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.AssetId && a.WorkspaceId == workspaceId, cancellationToken)
            .ConfigureAwait(false);

        if (asset is null)
        {
            throw new AssetNotFoundException(request.AssetId);
        }

        return ToDto(asset);
    }

    /// <summary>
    /// Mở thư mục chứa Asset trên hệ điều hành Windows an toàn.
    /// </summary>
    public async Task<OpenFolderResponse> OpenFolderAsync(
        OpenFolderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var workspaceId = NormalizeGuid(request.WorkspaceId, nameof(request.WorkspaceId));
        if (string.IsNullOrWhiteSpace(request.AssetId))
        {
            throw new ArgumentException("Mã assetId không được để trống.", nameof(request.AssetId));
        }

        await using var dbContext = new TiToAIDbContext(_dbOptions);

        var asset = await dbContext.Assets
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.AssetId && a.WorkspaceId == workspaceId, cancellationToken)
            .ConfigureAwait(false);

        if (asset is null)
        {
            throw new AssetNotFoundException(request.AssetId);
        }

        var rootPath = await _rootRegistry.ResolveRootPathAsync(asset.StorageRootId, cancellationToken).ConfigureAwait(false);
        var fullPath = StoragePathGuard.CombineAndVerify(rootPath, asset.RelativePath);
        var folderPath = Path.GetDirectoryName(fullPath);

        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
        {
            return new OpenFolderResponse(Success: false, FolderPath: folderPath);
        }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = folderPath,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Bỏ qua lỗi gọi shell nếu môi trường không cho phép
            }
        }

        return new OpenFolderResponse(Success: true, FolderPath: folderPath);
    }

    public static AssetDto ToDto(Asset asset)
    {
        return new AssetDto(
            AssetId: asset.Id,
            WorkspaceId: asset.WorkspaceId,
            ProjectId: asset.ProjectId,
            AssetType: asset.AssetType,
            StorageRootId: asset.StorageRootId,
            RelativePath: asset.RelativePath,
            ByteSize: asset.ByteSize,
            Sha256Hash: asset.Sha256Hash,
            Status: asset.Status,
            CreatedAtUtc: asset.CreatedAtUtc,
            MimeType: asset.MimeType
        );
    }

    private static async Task VerifySceneOwnershipAsync(
        TiToAIDbContext dbContext,
        string workspaceId,
        string projectId,
        string sceneId,
        CancellationToken cancellationToken)
    {
        var workspaceExists = await dbContext.Workspaces
            .AsNoTracking()
            .AnyAsync(w => w.Id == workspaceId, cancellationToken)
            .ConfigureAwait(false);

        if (!workspaceExists)
        {
            throw new WorkspaceNotFoundException(workspaceId);
        }

        var projectExists = await dbContext.Projects
            .AsNoTracking()
            .AnyAsync(p => p.Id == projectId && p.WorkspaceId == workspaceId, cancellationToken)
            .ConfigureAwait(false);

        if (!projectExists)
        {
            throw new ProjectNotFoundException(projectId);
        }

        var sceneExists = await dbContext.Scenes
            .AsNoTracking()
            .AnyAsync(s => s.Id == sceneId && s.ProjectId == projectId && s.WorkspaceId == workspaceId, cancellationToken)
            .ConfigureAwait(false);

        if (!sceneExists)
        {
            throw new SceneNotFoundException(sceneId);
        }
    }

    private const long MaxAudioFileSizeBytes = 100 * 1024 * 1024; // 100 MB

    private static void ValidateAudioFile(string filePath, string ext)
    {
        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length == 0)
        {
            throw new AssetValidationException("Tệp âm thanh nguồn rỗng (0 bytes).");
        }

        if (fileInfo.Length > MaxAudioFileSizeBytes)
        {
            throw new AssetValidationException($"Kích thước tệp âm thanh ({fileInfo.Length / (1024 * 1024)} MB) vượt quá giới hạn cho phép (tối đa 100 MB).");
        }

        byte[] header = new byte[12];
        int bytesRead;
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            bytesRead = fs.Read(header, 0, header.Length);
        }

        if (ext == ".wav")
        {
            if (bytesRead < 12 ||
                header[0] != 0x52 || header[1] != 0x49 || header[2] != 0x46 || header[3] != 0x46 || // "RIFF"
                header[8] != 0x57 || header[9] != 0x41 || header[10] != 0x56 || header[11] != 0x45)  // "WAVE"
            {
                throw new AssetValidationException("Tệp không đúng định dạng WAV hợp lệ (thiếu tiêu đề RIFF/WAVE).");
            }
        }
        else if (ext == ".mp3")
        {
            bool isId3 = bytesRead >= 3 && header[0] == 0x49 && header[1] == 0x44 && header[2] == 0x33; // "ID3"
            bool isMpegSync = bytesRead >= 2 && header[0] == 0xFF && (header[1] & 0xE0) == 0xE0; // MPEG sync frame
            if (!isId3 && !isMpegSync)
            {
                throw new AssetValidationException("Tệp không đúng định dạng MP3 hợp lệ (thiếu ID3 hoặc MPEG audio sync frame).");
            }
        }
    }

    private static string NormalizeGuid(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Tham số '{paramName}' không được để trống.", paramName);
        }

        if (Guid.TryParse(value, out var parsed))
        {
            return parsed.ToString("D").ToLowerInvariant();
        }

        return value.Trim();
    }
}
