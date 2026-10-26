using System.Collections.ObjectModel;
using TiToAI.Contracts.Prompts;

namespace TiToAI.Application.Prompts.Models;

/// <summary>
/// Bản chụp bất biến của kết quả render prompt template tại thời điểm đưa vào job.
/// Việc sửa đổi hoặc rollback template sau này hoàn toàn không làm thay đổi snapshot của job cũ.
/// </summary>
public sealed record PromptRenderSnapshot
{
    public string TemplateId { get; init; } = string.Empty;
    public string VersionId { get; init; } = string.Empty;
    public string RenderedText { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Variables { get; init; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
    public string CapturedAtUtc { get; init; } = string.Empty;

    public PromptRenderSnapshot() { }

    public PromptRenderSnapshot(
        string templateId,
        string versionId,
        string renderedText,
        IReadOnlyDictionary<string, string>? variables,
        string? capturedAtUtc = null)
    {
        TemplateId = templateId ?? throw new ArgumentNullException(nameof(templateId));
        VersionId = versionId ?? throw new ArgumentNullException(nameof(versionId));
        RenderedText = renderedText ?? throw new ArgumentNullException(nameof(renderedText));

        // Bản sao độc lập bất biến (defensive copy)
        var copy = variables != null
            ? new Dictionary<string, string>(variables, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        Variables = new ReadOnlyDictionary<string, string>(copy);

        CapturedAtUtc = capturedAtUtc ?? DateTimeOffset.UtcNow.ToString("o");
    }

    public PromptRenderSnapshotDto ToDto() =>
        new(
            TemplateId: TemplateId,
            VersionId: VersionId,
            RenderedText: RenderedText,
            Variables: Variables,
            CapturedAtUtc: CapturedAtUtc
        );

    public static PromptRenderSnapshot FromDto(PromptRenderSnapshotDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return new PromptRenderSnapshot(
            dto.TemplateId,
            dto.VersionId,
            dto.RenderedText,
            dto.Variables,
            dto.CapturedAtUtc
        );
    }
}
