using System;
using System.Collections.Generic;

namespace TiToAI.Contracts.Providers;

public sealed record JobHandoffDto(
    string JobId,
    string WorkspaceId,
    string ProjectId,
    string? SceneId,
    string TaskType,
    string ProviderKey,
    string PromptText,
    string? ConfigSnapshotJson,
    string? WorkflowVersion,
    long Revision,
    string ReasonCode,
    string Instructions,
    string ExpectedOutputSchema,
    bool HasUnresolvedSideEffect,
    string? ResultReference
);

public sealed record JobGetHandoffRequest(
    string WorkspaceId,
    string JobId
);

public sealed record JobGetHandoffResponseData(
    JobHandoffDto Handoff
);

public sealed record JobImportResultRequest(
    string WorkspaceId,
    string JobId,
    long ExpectedRevision,
    string ClientRequestId,
    string? TextResult = null,
    string? AssetId = null,
    bool ConfirmedNotReceived = false
);

public sealed record JobImportResultResponseData(
    string JobId,
    string State,
    long Revision,
    string? ResultId,
    string? AssetId,
    bool IsDuplicate,
    bool WasCanceled
);
