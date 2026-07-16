using System.Globalization;
using Microsoft.Data.Sqlite;
using NetworkHealthMonitor.Infrastructure;

namespace NetworkHealthMonitor.Data;

public sealed class DatabaseMigrationRunner : IDatabaseMigrationRunner
{
    public const string DeviceIncidentsEndedAtUtcMigrationId = "2026071601-device-incidents-ended-at-utc";
    private const string DeviceIncidentsTable = "DeviceIncidents";
    private const string EndedAtUtcColumn = "EndedAtUtc";
    private const string RecoveredAtUtcColumn = "RecoveredAtUtc";

    public async Task ApplyMigrationsAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        await ApplyDeviceIncidentsEndedAtUtcMigrationAsync(connection, cancellationToken);
    }

    private static async Task ApplyDeviceIncidentsEndedAtUtcMigrationAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            using var transaction = connection.BeginTransaction();
            var columns = await GetColumnsAsync(connection, transaction, DeviceIncidentsTable, cancellationToken);
            if (columns.Count == 0)
            {
                throw new InvalidOperationException($"Table '{DeviceIncidentsTable}' was not found.");
            }

            if (!columns.Contains(EndedAtUtcColumn))
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    $"ALTER TABLE {DeviceIncidentsTable} ADD COLUMN {EndedAtUtcColumn} TEXT NULL;",
                    cancellationToken);
                columns.Add(EndedAtUtcColumn);
            }

            if (columns.Contains(RecoveredAtUtcColumn))
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    $"""
                    UPDATE {DeviceIncidentsTable}
                    SET {EndedAtUtcColumn} = {RecoveredAtUtcColumn}
                    WHERE {EndedAtUtcColumn} IS NULL
                      AND {RecoveredAtUtcColumn} IS NOT NULL;
                    """,
                    cancellationToken);
            }

            await RecordMigrationAsync(connection, transaction, DeviceIncidentsEndedAtUtcMigrationId, cancellationToken);
            transaction.Commit();
        }
        catch (Exception ex)
        {
            AppErrorLogger.Log(
                ex,
                $"Database migration failed. DatabasePath={DatabasePaths.DatabaseFilePath}; MigrationId={DeviceIncidentsEndedAtUtcMigrationId}; Table={DeviceIncidentsTable}; Column={EndedAtUtcColumn}");
            throw;
        }
    }

    private static async Task<HashSet<string>> GetColumnsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info({tableName});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static async Task RecordMigrationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string version,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO SchemaMigrations (Version, AppliedAtUtc)
            VALUES (@Version, @AppliedAtUtc);
            """;
        command.Parameters.AddWithValue("@Version", version);
        command.Parameters.AddWithValue("@AppliedAtUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
