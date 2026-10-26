using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TiToAI.Infrastructure.Persistence.Interceptors;

namespace TiToAI.Infrastructure.Persistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTiToAIPersistence(
        this IServiceCollection services,
        string? databasePath = null)
    {
        var dbPath = databasePath ?? DatabasePathHelper.GetDefaultDatabasePath();
        var connectionString = DatabasePathHelper.BuildConnectionString(dbPath);

        services.AddSingleton(new SqlitePragmaInterceptor());
        services.AddSingleton<ISqliteDatabaseBootstrapper, SqliteDatabaseBootstrapper>();

        services.AddDbContextFactory<TiToAIDbContext>((serviceProvider, options) =>
        {
            var interceptor = serviceProvider.GetRequiredService<SqlitePragmaInterceptor>();
            options.UseSqlite(connectionString)
                   .AddInterceptors(interceptor);
        });

        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<TiToAIDbContext>>().CreateDbContext());

        return services;
    }
}
