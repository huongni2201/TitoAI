namespace TiToAI.Infrastructure.Persistence;

public record DatabaseBootstrapResult(
    string DatabasePath,
    string SqliteVersion,
    string JournalMode,
    bool ForeignKeysEnabled,
    int BusyTimeout,
    int SynchronousMode,
    IReadOnlyList<string> AppliedMigrations,
    bool IsBootstrapSuccessful);
