namespace TiToAI.Infrastructure.Persistence.Entities;

public class Account
{
    public string Id { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty; // MuseAI | TikTok
    public string AccountIdentity { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string Status { get; set; } = "Connected"; // Connected | SessionExpired | Unverified | Unlinked
    public string CreatedAtUtc { get; set; } = string.Empty;

    public Workspace? Workspace { get; set; }
}
