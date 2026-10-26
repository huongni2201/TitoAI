using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TiToAI.Infrastructure.Persistence.Entities;

namespace TiToAI.Infrastructure.Persistence.Configurations;

public class BrowserProfileConfiguration : IEntityTypeConfiguration<BrowserProfile>
{
    public void Configure(EntityTypeBuilder<BrowserProfile> builder)
    {
        builder.ToTable("BrowserProfile");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnName("id")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(p => p.Kind)
            .HasColumnName("kind")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(p => p.WorkspaceId)
            .HasColumnName("workspace_id")
            .HasColumnType("TEXT");

        builder.Property(p => p.AccountIdentity)
            .HasColumnName("account_identity")
            .HasColumnType("TEXT");

        builder.Property(p => p.VisibilityState)
            .HasColumnName("visibility_state")
            .HasColumnType("TEXT")
            .HasDefaultValue("Hidden")
            .IsRequired();

        builder.Property(p => p.SessionStatus)
            .HasColumnName("session_status")
            .HasColumnType("TEXT")
            .HasDefaultValue("Unverified")
            .IsRequired();

        builder.Property(p => p.OperationalState)
            .HasColumnName("operational_state")
            .HasColumnType("TEXT")
            .HasDefaultValue("Stopped")
            .IsRequired();

        builder.Property(p => p.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(p => p.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.HasOne(p => p.Workspace)
            .WithMany()
            .HasForeignKey(p => p.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => p.WorkspaceId)
            .HasDatabaseName("ix_browser_profile_workspace");
    }
}
