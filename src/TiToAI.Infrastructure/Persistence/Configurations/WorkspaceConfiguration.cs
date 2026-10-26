using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TiToAI.Infrastructure.Persistence.Entities;

namespace TiToAI.Infrastructure.Persistence.Configurations;

public class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public void Configure(EntityTypeBuilder<Workspace> builder)
    {
        builder.ToTable("Workspace");

        builder.HasKey(w => w.Id);

        builder.Property(w => w.Id)
            .HasColumnName("id")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(w => w.Name)
            .HasColumnName("name")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(w => w.ChannelHandle)
            .HasColumnName("channel_handle")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(w => w.Topic)
            .HasColumnName("topic")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(w => w.Status)
            .HasColumnName("status")
            .HasColumnType("TEXT")
            .HasDefaultValue("Active")
            .IsRequired();

        builder.Property(w => w.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(w => w.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("TEXT")
            .IsRequired();
    }
}
