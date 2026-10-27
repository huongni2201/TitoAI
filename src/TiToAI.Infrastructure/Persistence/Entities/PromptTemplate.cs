using System.ComponentModel.DataAnnotations.Schema;

namespace TiToAI.Infrastructure.Persistence.Entities;

public class PromptTemplate
{
    public string Id { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string? WorkspaceId { get; set; }
    public string TemplateText { get; set; } = string.Empty;
    public string VariableDefinitionsJson { get; set; } = "[]";
    public string CreatedAtUtc { get; set; } = string.Empty;
    public string UpdatedAtUtc { get; set; } = string.Empty;

    public Workspace? Workspace { get; set; }
    [NotMapped]
    public string? CurrentVersionId { get; set; }
    [NotMapped]
    public ICollection<PromptVersion> Versions { get; set; } = new List<PromptVersion>();
    [NotMapped]
    public PromptVersion? CurrentVersion { get; set; }
}
