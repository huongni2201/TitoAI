namespace TiToAI.Infrastructure.Persistence.Entities;

public class PromptVersion
{
    public string Id { get; set; } = string.Empty;
    public string TemplateId { get; set; } = string.Empty;
    public int VersionNumber { get; set; }
    public string TemplateText { get; set; } = string.Empty;
    public string VariableDefinitionsJson { get; set; } = "[]";
    public string? ChangeDescription { get; set; }
    public string CreatedAtUtc { get; set; } = string.Empty;

    public PromptTemplate? Template { get; set; }
}
