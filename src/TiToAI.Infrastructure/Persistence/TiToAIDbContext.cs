using Microsoft.EntityFrameworkCore;
using TiToAI.Infrastructure.Persistence.Entities;

namespace TiToAI.Infrastructure.Persistence;

public class TiToAIDbContext : DbContext
{
    public TiToAIDbContext(DbContextOptions<TiToAIDbContext> options)
        : base(options)
    {
    }

    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Scene> Scenes => Set<Scene>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<PromptTemplate> PromptTemplates => Set<PromptTemplate>();
    public DbSet<PromptVersion> PromptVersions => Set<PromptVersion>();
    public DbSet<BrowserProfile> BrowserProfiles => Set<BrowserProfile>();
    public DbSet<BrowserHandoffCheckpoint> BrowserHandoffCheckpoints => Set<BrowserHandoffCheckpoint>();
    public DbSet<ProviderAccount> ProviderAccounts => Set<ProviderAccount>();
    public DbSet<ExecutionBinding> ExecutionBindings => Set<ExecutionBinding>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TiToAIDbContext).Assembly);
    }
}
