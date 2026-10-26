using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TiToAI.Infrastructure.Persistence;

public class TiToAIDesignTimeDbContextFactory : IDesignTimeDbContextFactory<TiToAIDbContext>
{
    public TiToAIDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TiToAIDbContext>();
        optionsBuilder.UseSqlite("Data Source=:memory:");
        return new TiToAIDbContext(optionsBuilder.Options);
    }
}
