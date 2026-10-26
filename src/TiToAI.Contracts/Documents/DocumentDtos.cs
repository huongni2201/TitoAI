namespace TiToAI.Contracts.Documents;

public sealed record DocumentExportRequest(
    string WorkspaceId,
    string SceneId,
    string Format = "txt",
    string? CustomFileName = null
);

public sealed record DocumentExportResponse(
    string AssetId,
    string DisplayName,
    string StorageRootId,
    string RelativePath,
    long ByteSize
);
