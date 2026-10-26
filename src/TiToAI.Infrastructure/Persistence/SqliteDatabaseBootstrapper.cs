using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TiToAI.Infrastructure.Persistence.Interceptors;

namespace TiToAI.Infrastructure.Persistence;

public class SqliteDatabaseBootstrapper : ISqliteDatabaseBootstrapper
{
    private readonly ILogger<SqliteDatabaseBootstrapper>? _logger;

    public SqliteDatabaseBootstrapper(ILogger<SqliteDatabaseBootstrapper>? logger = null)
    {
        _logger = logger;
    }

    public async Task<DatabaseBootstrapResult> BootstrapAsync(
        string? databasePath = null,
        CancellationToken cancellationToken = default)
    {
        var dbPath = databasePath ?? DatabasePathHelper.GetDefaultDatabasePath();
        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connectionString = DatabasePathHelper.BuildConnectionString(dbPath);

        // 1. Initialize SQLite file and set WAL mode (persistent in DB file header)
        string initialJournalMode;
        using (var initConn = new SqliteConnection(connectionString))
        {
            await initConn.OpenAsync(cancellationToken).ConfigureAwait(false);

            using var walCmd = initConn.CreateCommand();
            walCmd.CommandText = "PRAGMA journal_mode = WAL;";
            var result = await walCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            initialJournalMode = result?.ToString() ?? string.Empty;
        }

        // 2. Run EF Core Migrations
        var optionsBuilder = new DbContextOptionsBuilder<TiToAIDbContext>();
        optionsBuilder.UseSqlite(connectionString);
        optionsBuilder.AddInterceptors(new SqlitePragmaInterceptor());
        optionsBuilder.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));

        IReadOnlyList<string> appliedMigrations;
        using (var dbContext = new TiToAIDbContext(optionsBuilder.Options))
        {
            var pending = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToList();
            await dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            appliedMigrations = pending.AsReadOnly();
        }

        // 3. Verify SQLite Engine Version and PRAGMAs on a fresh connection opened via TiToAIDbContext with SqlitePragmaInterceptor
        string sqliteVersion;
        string journalMode;
        bool foreignKeysEnabled;
        int busyTimeout;
        int synchronousMode;

        var verifyOptionsBuilder = new DbContextOptionsBuilder<TiToAIDbContext>();
        verifyOptionsBuilder.UseSqlite(connectionString);
        verifyOptionsBuilder.AddInterceptors(new SqlitePragmaInterceptor());

        await using (var verifyDbContext = new TiToAIDbContext(verifyOptionsBuilder.Options))
        {
            await verifyDbContext.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var verifyConn = verifyDbContext.Database.GetDbConnection();

            using (var verCmd = verifyConn.CreateCommand())
            {
                verCmd.CommandText = "SELECT sqlite_version();";
                sqliteVersion = (await verCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))?.ToString() ?? "unknown";
            }

            using (var jmCmd = verifyConn.CreateCommand())
            {
                jmCmd.CommandText = "PRAGMA journal_mode;";
                journalMode = (await jmCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))?.ToString() ?? "unknown";
            }

            using (var fkCmd = verifyConn.CreateCommand())
            {
                fkCmd.CommandText = "PRAGMA foreign_keys;";
                var fk = await fkCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                foreignKeysEnabled = Convert.ToInt32(fk) == 1;
            }

            using (var btCmd = verifyConn.CreateCommand())
            {
                btCmd.CommandText = "PRAGMA busy_timeout;";
                var bt = await btCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                busyTimeout = Convert.ToInt32(bt);
            }

            using (var synCmd = verifyConn.CreateCommand())
            {
                synCmd.CommandText = "PRAGMA synchronous;";
                var syn = await synCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                synchronousMode = Convert.ToInt32(syn);
            }

            await verifyDbContext.Database.CloseConnectionAsync().ConfigureAwait(false);
        }

        _logger?.LogInformation(
            "SQLite bootstrap completed for {Path}: SQLite {Version}, WAL={JournalMode}, FK={FK}, BusyTimeout={Timeout}ms, Synchronous={Sync}",
            dbPath, sqliteVersion, journalMode, foreignKeysEnabled, busyTimeout, synchronousMode);

        Console.WriteLine($"[TiToAI SQLite Bootstrap] Database: {dbPath}");
        Console.WriteLine($"[TiToAI SQLite Bootstrap] SQLite Version: {sqliteVersion}");
        Console.WriteLine($"[TiToAI SQLite Bootstrap] Journal Mode: {journalMode}");
        Console.WriteLine($"[TiToAI SQLite Bootstrap] Foreign Keys: {(foreignKeysEnabled ? "ON" : "OFF")}");
        Console.WriteLine($"[TiToAI SQLite Bootstrap] Busy Timeout: {busyTimeout}ms");
        Console.WriteLine($"[TiToAI SQLite Bootstrap] Synchronous: {synchronousMode}");

        var isSuccess = string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase)
                        && foreignKeysEnabled
                        && busyTimeout == 5000
                        && synchronousMode == 1;

        return new DatabaseBootstrapResult(
            DatabasePath: dbPath,
            SqliteVersion: sqliteVersion,
            JournalMode: journalMode,
            ForeignKeysEnabled: foreignKeysEnabled,
            BusyTimeout: busyTimeout,
            SynchronousMode: synchronousMode,
            AppliedMigrations: appliedMigrations,
            IsBootstrapSuccessful: isSuccess);
    }
}
