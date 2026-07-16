using Microsoft.Data.Sqlite;

namespace NetworkHealthMonitor.Data;

public interface IDatabaseMigrationRunner
{
    Task ApplyMigrationsAsync(SqliteConnection connection, CancellationToken cancellationToken = default);
}
