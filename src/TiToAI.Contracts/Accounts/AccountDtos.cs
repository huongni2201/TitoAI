using System.Text.Json.Serialization;

namespace TiToAI.Contracts.Accounts;

public sealed record AccountDto(
    [property: JsonPropertyName("accountId")] string AccountId,
    [property: JsonPropertyName("workspaceId")] string WorkspaceId,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("accountIdentity")] string AccountIdentity,
    [property: JsonPropertyName("profileId")] string ProfileId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("createdAtUtc")] string CreatedAtUtc);

public sealed record AccountListByWorkspaceRequest(
    [property: JsonPropertyName("workspaceId")] string WorkspaceId);

public sealed record AccountListByWorkspaceResponseData(
    [property: JsonPropertyName("accounts")] IReadOnlyList<AccountDto> Accounts);

public sealed record AccountLinkRequest(
    [property: JsonPropertyName("workspaceId")] string WorkspaceId,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("profileId")] string ProfileId);

public sealed record AccountLinkResponseData(
    [property: JsonPropertyName("account")] AccountDto Account);

public sealed record AccountUnlinkRequest(
    [property: JsonPropertyName("workspaceId")] string WorkspaceId,
    [property: JsonPropertyName("accountId")] string AccountId);

public sealed record AccountUnlinkResponseData(
    [property: JsonPropertyName("accountId")] string AccountId,
    [property: JsonPropertyName("status")] string Status);
