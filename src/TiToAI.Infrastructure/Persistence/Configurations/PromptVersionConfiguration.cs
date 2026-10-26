using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TiToAI.Infrastructure.Persistence.Entities;

namespace TiToAI.Infrastructure.Persistence.Configurations;

public class PromptVersionConfiguration : IEntityTypeConfiguration<PromptVersion>
{
    public void Configure(EntityTypeBuilder<PromptVersion> builder)
    {
        builder.ToTable("PromptVersion");

        builder.HasKey(v => v.Id);

        builder.Property(v => v.Id)
            .HasColumnName("id")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(v => v.TemplateId)
            .HasColumnName("template_id")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(v => v.VersionNumber)
            .HasColumnName("version_number")
            .HasColumnType("INTEGER")
            .IsRequired();

        builder.Property(v => v.TemplateText)
            .HasColumnName("template_text")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(v => v.VariableDefinitionsJson)
            .HasColumnName("variable_definitions_json")
            .HasColumnType("TEXT")
            .HasDefaultValue("[]")
            .IsRequired();

        builder.Property(v => v.ChangeDescription)
            .HasColumnName("change_description")
            .HasColumnType("TEXT");

        builder.Property(v => v.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.HasOne(v => v.Template)
            .WithMany(t => t.Versions)
            .HasForeignKey(v => v.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(v => new { v.TemplateId, v.VersionNumber })
            .IsUnique()
            .HasDatabaseName("uq_prompt_version_template_version");

        builder.HasIndex(v => new { v.Id, v.TemplateId })
            .IsUnique()
            .HasDatabaseName("uq_prompt_version_id_template");
    }
}
