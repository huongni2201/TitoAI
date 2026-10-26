using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TiToAI.Infrastructure.Persistence.Entities;

namespace TiToAI.Infrastructure.Persistence.Configurations;

public class PromptTemplateConfiguration : IEntityTypeConfiguration<PromptTemplate>
{
    public void Configure(EntityTypeBuilder<PromptTemplate> builder)
    {
        builder.ToTable("PromptTemplate");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasColumnName("id")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(t => t.Purpose)
            .HasColumnName("purpose")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(t => t.WorkspaceId)
            .HasColumnName("workspace_id")
            .HasColumnType("TEXT");

        builder.Property(t => t.CurrentVersionId)
            .HasColumnName("current_version_id")
            .HasColumnType("TEXT");

        builder.Property(t => t.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(t => t.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.HasOne(t => t.Workspace)
            .WithMany()
            .HasForeignKey(t => t.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        // Composite foreign key enforcing that CurrentVersionId belongs to this template (Id)
        builder.HasOne(t => t.CurrentVersion)
            .WithMany()
            .HasForeignKey(t => new { t.CurrentVersionId, t.Id })
            .HasPrincipalKey(v => new { v.Id, v.TemplateId })
            .OnDelete(DeleteBehavior.Restrict);

        // Global templates unique index: purpose when workspace_id IS NULL
        builder.HasIndex(t => t.Purpose)
            .IsUnique()
            .HasFilter("workspace_id IS NULL")
            .HasDatabaseName("uq_prompt_template_global_purpose");

        // Workspace overrides unique index: (workspace_id, purpose) when workspace_id IS NOT NULL
        builder.HasIndex(t => new { t.WorkspaceId, t.Purpose })
            .IsUnique()
            .HasFilter("workspace_id IS NOT NULL")
            .HasDatabaseName("uq_prompt_template_workspace_purpose");
    }
}
