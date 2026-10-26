using System.Text.Json.Serialization;

namespace TiToAI.Contracts.Browser;

public sealed record BrowserProfileDto(
    [property: JsonPropertyName("profileId")] string ProfileId,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("accountIdentity")] string? AccountIdentity,
    [property: JsonPropertyName("workspaceId")] string? WorkspaceId,
    [property: JsonPropertyName("visibilityState")] string VisibilityState,
    [property: JsonPropertyName("sessionStatus")] string SessionStatus,
    [property: JsonPropertyName("operationalState")] string OperationalState,
    [property: JsonPropertyName("activeJobCount")] int ActiveJobCount,
    [property: JsonPropertyName("activeTabs")] IReadOnlyList<string> ActiveTabs);

public sealed record BrowserListProfilesRequest(
    [property: JsonPropertyName("workspaceId")] string? WorkspaceId);

public sealed record BrowserListProfilesResponseData(
    [property: JsonPropertyName("profiles")] IReadOnlyList<BrowserProfileDto> Profiles);

public sealed record BrowserRequestLoginRequest(
    [property: JsonPropertyName("profileId")] string ProfileId);

public sealed record BrowserRequestLoginResponseData(
    [property: JsonPropertyName("queued")] bool Queued,
    [property: JsonPropertyName("visibilityState")] string VisibilityState,
    [property: JsonPropertyName("currentHolder")] string? CurrentHolder);

public sealed record BrowserCancelLoginRequest(
    [property: JsonPropertyName("profileId")] string ProfileId);

public sealed record BrowserCancelLoginResponseData(
    [property: JsonPropertyName("canceled")] bool Canceled);

public sealed record BrowserCheckSessionRequest(
    [property: JsonPropertyName("profileId")] string ProfileId);

public sealed record BrowserCheckSessionResponseData(
    [property: JsonPropertyName("sessionStatus")] string SessionStatus,
    [property: JsonPropertyName("identityMatches")] bool? IdentityMatches);

public sealed record BrowserProfileStateChangedPayload(
    [property: JsonPropertyName("profileId")] string ProfileId,
    [property: JsonPropertyName("visibilityState")] string VisibilityState,
    [property: JsonPropertyName("sessionStatus")] string SessionStatus,
    [property: JsonPropertyName("operationalState")] string OperationalState,
    [property: JsonPropertyName("reasonCode")] string? ReasonCode);
