namespace TiToAI.Contracts.Assets;

public sealed record AssetDto(
    string AssetId,
    string WorkspaceId,
    string? ProjectId,
    string AssetType,
    string StorageRootId,
    string RelativePath,
    long ByteSize,
    string Sha256Hash,
    string Status,
    string CreatedAtUtc,
    string? MimeType = null
);

public sealed record ImportAudioRequest(
    string WorkspaceId,
    string ProjectId,
    string SceneId,
    string? SourceFilePath = null // Dùng khi programmatic import/smoke test hoặc truyền trực tiếp từ dialog
);

public sealed record ImportAudioResponse(
    AssetDto? Asset
);

public sealed record SelectReferenceImageRequest(
    string WorkspaceId,
    string ProjectId,
    string SceneId,
    string? SourceFilePath = null
);

public sealed record SelectReferenceImageResponse(
    AssetDto? Asset
);

public sealed record GetAssetMetadataRequest(
    string AssetId,
    string WorkspaceId
);

public sealed record OpenFolderRequest(
    string AssetId,
    string WorkspaceId
);

public sealed record OpenFolderResponse(
    bool Success,
    string? FolderPath = null
);
