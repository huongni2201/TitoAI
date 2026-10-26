namespace TiToAI.Infrastructure.Persistence.Entities;

public class BrowserProfile
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? WorkspaceId { get; set; }
    public string? AccountIdentity { get; set; }
    public string VisibilityState { get; set; } = "Hidden";
    public string SessionStatus { get; set; } = "Unverified";
    public string OperationalState { get; set; } = "Stopped";
    public string CreatedAtUtc { get; set; } = string.Empty;
    public string UpdatedAtUtc { get; set; } = string.Empty;

    public Workspace? Workspace { get; set; }
}
