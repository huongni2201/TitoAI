using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TiToAI.Infrastructure.Persistence.Entities;

namespace TiToAI.Infrastructure.Persistence.Configurations;

public class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("Account");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasColumnName("id")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(a => a.WorkspaceId)
            .HasColumnName("workspace_id")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(a => a.Provider)
            .HasColumnName("provider")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(a => a.AccountIdentity)
            .HasColumnName("account_identity")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(a => a.ProfileId)
            .HasColumnName("profile_id")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(a => a.Status)
            .HasColumnName("status")
            .HasColumnType("TEXT")
            .HasDefaultValue("Connected")
            .IsRequired();

        builder.Property(a => a.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.HasOne(a => a.Workspace)
            .WithMany()
            .HasForeignKey(a => a.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => new { a.WorkspaceId, a.Provider })
            .IsUnique()
            .HasFilter("status != 'Unlinked'")
            .HasDatabaseName("uq_account_workspace_provider");

        builder.HasIndex(a => new { a.Provider, a.AccountIdentity })
            .IsUnique()
            .HasFilter("status != 'Unlinked'")
            .HasDatabaseName("uq_account_provider_identity");
    }
}
