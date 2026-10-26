using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TiToAI.Infrastructure.Persistence.Entities;

namespace TiToAI.Infrastructure.Persistence.Configurations;

public class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.ToTable("Asset");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasColumnName("id")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(a => a.WorkspaceId)
            .HasColumnName("workspace_id")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(a => a.ProjectId)
            .HasColumnName("project_id")
            .HasColumnType("TEXT");

        builder.Property(a => a.StorageRootId)
            .HasColumnName("storage_root_id")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(a => a.RelativePath)
            .HasColumnName("relative_path")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(a => a.AssetType)
            .HasColumnName("asset_type")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(a => a.MimeType)
            .HasColumnName("mime_type")
            .HasColumnType("TEXT");

        builder.Property(a => a.ByteSize)
            .HasColumnName("byte_size")
            .HasColumnType("INTEGER")
            .IsRequired();

        builder.Property(a => a.Sha256Hash)
            .HasColumnName("sha256_hash")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(a => a.Status)
            .HasColumnName("status")
            .HasColumnType("TEXT")
            .HasDefaultValue("Linked")
            .IsRequired();

        builder.Property(a => a.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.HasOne(a => a.Workspace)
            .WithMany()
            .HasForeignKey(a => a.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Project)
            .WithMany()
            .HasForeignKey(a => new { a.ProjectId, a.WorkspaceId })
            .HasPrincipalKey(p => new { p.Id, p.WorkspaceId })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(a => new { a.Id, a.WorkspaceId })
            .IsUnique()
            .HasDatabaseName("uq_asset_id_workspace");

        builder.HasIndex(a => new { a.ProjectId, a.WorkspaceId })
            .HasDatabaseName("ix_asset_project_workspace");
    }
}
