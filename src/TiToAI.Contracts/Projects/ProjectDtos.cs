using System.Text.Json.Serialization;

namespace TiToAI.Contracts.Projects;

/// <summary>
/// Bản ghi tóm tắt Dự án phục vụ danh sách hiển thị.
/// </summary>
public sealed record ProjectSummaryDto(
    [property: JsonPropertyName("projectId")] string ProjectId,
    [property: JsonPropertyName("workspaceId")] string WorkspaceId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("sceneCount")] int SceneCount,
    [property: JsonPropertyName("createdAtUtc")] string CreatedAtUtc,
    [property: JsonPropertyName("updatedAtUtc")] string UpdatedAtUtc);

/// <summary>
/// Bản ghi chi tiết Dự án.
/// </summary>
public sealed record ProjectDetailDto(
    [property: JsonPropertyName("projectId")] string ProjectId,
    [property: JsonPropertyName("workspaceId")] string WorkspaceId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("createdAtUtc")] string CreatedAtUtc,
    [property: JsonPropertyName("updatedAtUtc")] string UpdatedAtUtc);

/// <summary>
/// Yêu cầu tạo mới Dự án.
/// </summary>
public sealed record ProjectCreateRequest(
    [property: JsonPropertyName("workspaceId")] string WorkspaceId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description);

public sealed record ProjectCreateResponseData(
    [property: JsonPropertyName("projectId")] string ProjectId);

/// <summary>
/// Yêu cầu cập nhật Dự án.
/// </summary>
public sealed record ProjectUpdateRequest(
    [property: JsonPropertyName("workspaceId")] string WorkspaceId,
    [property: JsonPropertyName("projectId")] string ProjectId,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("status")] string? Status);

public sealed record ProjectUpdateResponseData(
    [property: JsonPropertyName("project")] ProjectDetailDto Project);

/// <summary>
/// Yêu cầu xóa Dự án.
/// </summary>
public sealed record ProjectDeleteRequest(
    [property: JsonPropertyName("workspaceId")] string WorkspaceId,
    [property: JsonPropertyName("projectId")] string ProjectId);

public sealed record ProjectDeleteResponseData(
    [property: JsonPropertyName("projectId")] string ProjectId,
    [property: JsonPropertyName("success")] bool Success);

/// <summary>
/// Yêu cầu danh sách Dự án theo Workspace.
/// </summary>
public sealed record ProjectListByWorkspaceRequest(
    [property: JsonPropertyName("workspaceId")] string WorkspaceId);

public sealed record ProjectListByWorkspaceResponseData(
    [property: JsonPropertyName("projects")] IReadOnlyList<ProjectSummaryDto> Projects);

/// <summary>
/// Yêu cầu lấy chi tiết Dự án.
/// </summary>
public sealed record ProjectGetRequest(
    [property: JsonPropertyName("workspaceId")] string WorkspaceId,
    [property: JsonPropertyName("projectId")] string ProjectId);

public sealed record ProjectGetResponseData(
    [property: JsonPropertyName("project")] ProjectDetailDto Project);
