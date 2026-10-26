using System.Text.Json.Serialization;

namespace TiToAI.Contracts.Workspaces;

public sealed record WorkspaceSummaryDto(
    [property: JsonPropertyName("workspaceId")] string WorkspaceId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("channelHandle")] string ChannelHandle,
    [property: JsonPropertyName("topic")] string Topic,
    [property: JsonPropertyName("status")] string Status);

public sealed record WorkspaceDetailDto(
    [property: JsonPropertyName("workspaceId")] string WorkspaceId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("channelHandle")] string ChannelHandle,
    [property: JsonPropertyName("topic")] string Topic,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("createdAtUtc")] string CreatedAtUtc,
    [property: JsonPropertyName("updatedAtUtc")] string UpdatedAtUtc);

public sealed record WorkspaceListResponseData(
    [property: JsonPropertyName("workspaces")] IReadOnlyList<WorkspaceSummaryDto> Workspaces);

public sealed record WorkspaceGetRequest(
    [property: JsonPropertyName("workspaceId")] string WorkspaceId);

public sealed record WorkspaceGetResponseData(
    [property: JsonPropertyName("workspace")] WorkspaceDetailDto Workspace);

public sealed record WorkspaceCreateRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("channelHandle")] string ChannelHandle,
    [property: JsonPropertyName("topic")] string Topic);

public sealed record WorkspaceCreateResponseData(
    [property: JsonPropertyName("workspaceId")] string WorkspaceId);

public sealed record WorkspaceUpdateRequest(
    [property: JsonPropertyName("workspaceId")] string WorkspaceId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("channelHandle")] string ChannelHandle,
    [property: JsonPropertyName("topic")] string Topic);

public sealed record WorkspaceUpdateResponseData(
    [property: JsonPropertyName("workspace")] WorkspaceDetailDto Workspace);

public sealed record WorkspaceDeleteRequest(
    [property: JsonPropertyName("workspaceId")] string WorkspaceId);

public sealed record WorkspaceDeleteResponseData(
    [property: JsonPropertyName("workspaceId")] string WorkspaceId,
    [property: JsonPropertyName("deleted")] bool Deleted);

public sealed record WorkspaceSetStatusRequest(
    [property: JsonPropertyName("workspaceId")] string WorkspaceId,
    [property: JsonPropertyName("status")] string Status);

public sealed record WorkspaceSetStatusResponseData(
    [property: JsonPropertyName("workspaceId")] string WorkspaceId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("updatedAtUtc")] string UpdatedAtUtc);

public sealed record WorkspaceStatusChangedPayload(
    [property: JsonPropertyName("workspaceId")] string WorkspaceId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("updatedAtUtc")] string UpdatedAtUtc);
