using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TiToAI.Infrastructure.Persistence;

var root = Path.Combine(Path.GetTempPath(), "titoai_final_schema_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    await VerifySavedSqlMatchesEfModelAsync();

    var bootstrapper = new SqliteDatabaseBootstrapper();
    var freshPath = Path.Combine(root, "fresh.db");
    var fresh = await bootstrapper.BootstrapAsync(freshPath);
    Assert(fresh.IsBootstrapSuccessful && fresh.SchemaVersion == 3 && !fresh.DatabaseReset,
        "A fresh database must initialize directly from schema version 3.");
    await VerifyFinalSchemaAsync(freshPath);

    var legacyPath = Path.Combine(root, "legacy.db");
    await CreateLegacyFixtureAsync(legacyPath);
    var reset = await bootstrapper.BootstrapAsync(legacyPath);
    Assert(reset.IsBootstrapSuccessful && reset.SchemaVersion == 3 && reset.DatabaseReset,
        "A legacy database must be backed up and reset to schema version 3.");
    await VerifyFinalSchemaAsync(legacyPath);
    await VerifyLegacyBackupAsync(root);

    var previousPath = Path.Combine(root, "previous.db");
    await CreateVersion2FixtureAsync(previousPath);
    var upgrade = await bootstrapper.BootstrapAsync(previousPath);
    Assert(upgrade.SchemaVersion == 3 && !upgrade.DatabaseReset, "Version 2 must upgrade without resetting data.");
    await using (var previous = new SqliteConnection(DatabasePathHelper.BuildConnectionString(previousPath)))
    {
        await previous.OpenAsync();
        await using var command = previous.CreateCommand();
        command.CommandText = "SELECT name, is_active, template_text FROM PromptTemplate WHERE id = 'old-prompt';";
        await using var reader = await command.ExecuteReaderAsync();
        Assert(await reader.ReadAsync() && reader.GetString(0) == "Mẫu hiện tại" && reader.GetInt32(1) == 1 && reader.GetString(2) == "Nội dung cũ",
            "Existing prompt must remain active with its content intact.");
    }

    Console.WriteLine("Final SQL/model parity, fresh bootstrap, legacy reset, and backup: PASS");
    return 0;
}
finally
{
    SqliteConnection.ClearAllPools();
    Directory.Delete(root, recursive: true);
}

static async Task VerifySavedSqlMatchesEfModelAsync()
{
    var repositoryRoot = Directory.GetCurrentDirectory();
    var sqlPath = Path.Combine(repositoryRoot, "src", "TiToAI.Infrastructure", "Persistence", "Sql", "final-schema.sql");
    var savedSql = await File.ReadAllTextAsync(sqlPath);
    var options = new DbContextOptionsBuilder<TiToAIDbContext>().UseSqlite("Data Source=:memory:").Options;
    await using var db = new TiToAIDbContext(options);
    var generatedSql = db.Database.GenerateCreateScript() + Environment.NewLine + "PRAGMA user_version = 3;" + Environment.NewLine;
    var saved = NormalizeSql(savedSql).Split('\n');
    var generated = NormalizeSql(generatedSql).Split('\n');
    var difference = Enumerable.Range(0, Math.Min(saved.Length, generated.Length)).FirstOrDefault(i => saved[i] != generated[i], -1);
    Assert(difference < 0 && saved.Length == generated.Length,
        difference < 0 ? "Saved SQL has a different number of lines." : $"SQL mismatch at line {difference + 1}: saved '{saved[difference]}', generated '{generated[difference]}'.");
}

static async Task VerifyFinalSchemaAsync(string path)
{
    await using var connection = new SqliteConnection(DatabasePathHelper.BuildConnectionString(path));
    await connection.OpenAsync();

    await using (var versionCommand = connection.CreateCommand())
    {
        versionCommand.CommandText = "PRAGMA user_version;";
        Assert(Convert.ToInt32(await versionCommand.ExecuteScalarAsync()) == 3, "Database schema version must equal 3.");
    }

    await using (var legacyTablesCommand = connection.CreateCommand())
    {
        legacyTablesCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name = '__EFMigrationsHistory';";
        Assert(Convert.ToInt32(await legacyTablesCommand.ExecuteScalarAsync()) == 0, "EF migration history must be absent.");
    }

    foreach (var table in new[] { "Project", "Scene", "Asset", "Job", "ProviderAccount", "VideoRevision", "PublishRecord" })
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\");";
        await using var reader = await command.ExecuteReaderAsync();
        var found = false;
        while (await reader.ReadAsync())
        {
            found = true;
        }
        Assert(found, $"Required table {table} must exist.");
    }
}

static async Task CreateVersion2FixtureAsync(string path)
{
    var sql = await File.ReadAllTextAsync(Path.Combine(Directory.GetCurrentDirectory(), "src", "TiToAI.Infrastructure", "Persistence", "Sql", "final-schema.sql"));
    sql = sql.Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace("    \"name\" TEXT NOT NULL,\n    \"is_active\" INTEGER NOT NULL,\n    \"template_text\"", "    \"template_text\"", StringComparison.Ordinal)
        .Replace("CREATE UNIQUE INDEX \"uq_prompt_template_active_purpose\" ON \"PromptTemplate\" (\"purpose\") WHERE \"is_active\" = 1;", "CREATE UNIQUE INDEX \"uq_prompt_template_global_purpose\" ON \"PromptTemplate\" (\"purpose\");", StringComparison.Ordinal)
        .Replace("PRAGMA user_version = 3;", "PRAGMA user_version = 2;", StringComparison.Ordinal);
    await using var connection = new SqliteConnection(DatabasePathHelper.BuildConnectionString(path));
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = sql + "\nINSERT INTO PromptTemplate (id,purpose,template_text,variable_definitions_json,created_at_utc,updated_at_utc) VALUES ('old-prompt','Script','Nội dung cũ','[]','now','now');";
    await command.ExecuteNonQueryAsync();
}

static async Task CreateLegacyFixtureAsync(string path)
{
    await using var connection = new SqliteConnection(DatabasePathHelper.BuildConnectionString(path));
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = "CREATE TABLE LegacyTable (id TEXT PRIMARY KEY); INSERT INTO LegacyTable(id) VALUES ('legacy-test-row');";
    await command.ExecuteNonQueryAsync();
}

static async Task VerifyLegacyBackupAsync(string root)
{
    var backups = Directory.GetFiles(root, "legacy.db.pre-reset-*.bak");
    Assert(backups.Length == 1, "Exactly one pre-reset backup must be created.");
    await using var connection = new SqliteConnection(DatabasePathHelper.BuildConnectionString(backups[0]));
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT id FROM LegacyTable;";
    Assert((string?)await command.ExecuteScalarAsync() == "legacy-test-row", "Pre-reset backup must preserve legacy data.");
}

static string NormalizeSql(string sql)
    => string.Join('\n', sql.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(line => line.TrimEnd())).Trim();

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
