using System.Text.Json.Serialization;

namespace TiToAI.Contracts.Prompts;

public sealed record PromptTemplateDto(
    [property: JsonPropertyName("templateId")] string TemplateId,
    [property: JsonPropertyName("purpose")] string Purpose,
    [property: JsonPropertyName("workspaceId")] string? WorkspaceId,
    [property: JsonPropertyName("isOverride")] bool IsOverride,
    [property: JsonPropertyName("currentVersionId")] string? CurrentVersionId,
    [property: JsonPropertyName("currentVersionNumber")] int? CurrentVersionNumber,
    [property: JsonPropertyName("createdAtUtc")] string CreatedAtUtc,
    [property: JsonPropertyName("updatedAtUtc")] string UpdatedAtUtc);

public sealed record PromptVersionDto(
    [property: JsonPropertyName("versionId")] string VersionId,
    [property: JsonPropertyName("templateId")] string TemplateId,
    [property: JsonPropertyName("versionNumber")] int VersionNumber,
    [property: JsonPropertyName("templateText")] string TemplateText,
    [property: JsonPropertyName("variableDefinitionsJson")] string VariableDefinitionsJson,
    [property: JsonPropertyName("changeDescription")] string? ChangeDescription,
    [property: JsonPropertyName("createdAtUtc")] string CreatedAtUtc);

public sealed record PromptListTemplatesRequest(
    [property: JsonPropertyName("workspaceId")] string? WorkspaceId);

public sealed record PromptListTemplatesResponseData(
    [property: JsonPropertyName("templates")] IReadOnlyList<PromptTemplateDto> Templates);

public sealed record PromptGetTemplateRequest(
    [property: JsonPropertyName("templateId")] string? TemplateId,
    [property: JsonPropertyName("purpose")] string? Purpose,
    [property: JsonPropertyName("workspaceId")] string? WorkspaceId);

public sealed record PromptGetTemplateResponseData(
    [property: JsonPropertyName("template")] PromptTemplateDto Template,
    [property: JsonPropertyName("currentVersion")] PromptVersionDto? CurrentVersion);

public sealed record PromptListVersionsRequest(
    [property: JsonPropertyName("templateId")] string TemplateId);

public sealed record PromptListVersionsResponseData(
    [property: JsonPropertyName("versions")] IReadOnlyList<PromptVersionDto> Versions);

public sealed record PromptCreateVersionRequest(
    [property: JsonPropertyName("purpose")] string Purpose,
    [property: JsonPropertyName("workspaceId")] string? WorkspaceId,
    [property: JsonPropertyName("templateText")] string TemplateText,
    [property: JsonPropertyName("variableDefinitionsJson")] string? VariableDefinitionsJson,
    [property: JsonPropertyName("changeDescription")] string? ChangeDescription);

public sealed record PromptCreateVersionResponseData(
    [property: JsonPropertyName("template")] PromptTemplateDto Template,
    [property: JsonPropertyName("version")] PromptVersionDto Version);

public sealed record PromptPreviewRequest(
    [property: JsonPropertyName("templateId")] string? TemplateId,
    [property: JsonPropertyName("purpose")] string? Purpose,
    [property: JsonPropertyName("workspaceId")] string? WorkspaceId,
    [property: JsonPropertyName("templateText")] string? TemplateText,
    [property: JsonPropertyName("variables")] IReadOnlyDictionary<string, string>? Variables);

public sealed record PromptPreviewResponseData(
    [property: JsonPropertyName("renderedText")] string RenderedText,
    [property: JsonPropertyName("templateId")] string? TemplateId,
    [property: JsonPropertyName("versionId")] string? VersionId,
    [property: JsonPropertyName("variableCount")] int VariableCount,
    [property: JsonPropertyName("isOverride")] bool IsOverride);

public sealed record PromptRollbackVersionRequest(
    [property: JsonPropertyName("templateId")] string TemplateId,
    [property: JsonPropertyName("targetVersionId")] string TargetVersionId);

public sealed record PromptRollbackVersionResponseData(
    [property: JsonPropertyName("template")] PromptTemplateDto Template,
    [property: JsonPropertyName("currentVersion")] PromptVersionDto CurrentVersion);

public sealed record PromptDeleteOverrideRequest(
    [property: JsonPropertyName("templateId")] string TemplateId);

public sealed record PromptDeleteOverrideResponseData(
    [property: JsonPropertyName("templateId")] string TemplateId,
    [property: JsonPropertyName("deleted")] bool Deleted);

public sealed record PromptRenderSnapshotDto(
    [property: JsonPropertyName("templateId")] string TemplateId,
    [property: JsonPropertyName("versionId")] string VersionId,
    [property: JsonPropertyName("renderedText")] string RenderedText,
    [property: JsonPropertyName("variables")] IReadOnlyDictionary<string, string> Variables,
    [property: JsonPropertyName("capturedAtUtc")] string CapturedAtUtc);
