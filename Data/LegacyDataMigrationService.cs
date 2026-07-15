using System.IO;
using Microsoft.Data.Sqlite;
using NetworkHealthMonitor.Infrastructure;

namespace NetworkHealthMonitor.Data;

public static class LegacyDataMigrationService
{
    public static async Task<LegacyMigrationResult> MigrateIfNeededAsync()
    {
        DatabasePaths.EnsureDirectories();

        if (File.Exists(DatabasePaths.DatabaseFilePath))
        {
            return LegacyMigrationResult.Skipped("ProgramData database already exists.");
        }

        if (File.Exists(DatabasePaths.LegacyProgramDataDatabaseFilePath))
        {
            return await CopyLegacyDatabaseAsync(
                DatabasePaths.LegacyProgramDataDatabaseFilePath,
                DatabasePaths.LegacyProgramDataSettingsFilePath,
                "programdata-root");
        }

        if (!File.Exists(DatabasePaths.LegacyDatabaseFilePath))
        {
            return LegacyMigrationResult.Skipped("No legacy LocalAppData database was found.");
        }

        return await CopyLegacyDatabaseAsync(
            DatabasePaths.LegacyDatabaseFilePath,
            DatabasePaths.LegacySettingsFilePath,
            "legacy-localappdata");
    }

    private static async Task<LegacyMigrationResult> CopyLegacyDatabaseAsync(
        string sourceDatabasePath,
        string sourceSettingsPath,
        string sourceName)
    {
        if (!await CanOpenSqliteDatabaseAsync(sourceDatabasePath))
        {
            return LegacyMigrationResult.Failed("Legacy database could not be opened as SQLite.");
        }

        var backupPath = Path.Combine(
            DatabasePaths.BackupDirectory,
            $"{sourceName}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.db");

        File.Copy(sourceDatabasePath, backupPath, overwrite: false);
        File.Copy(sourceDatabasePath, DatabasePaths.DatabaseFilePath, overwrite: false);

        if (!await CanOpenSqliteDatabaseAsync(DatabasePaths.DatabaseFilePath))
        {
            File.Delete(DatabasePaths.DatabaseFilePath);
            return LegacyMigrationResult.Failed("Copied ProgramData database could not be opened.");
        }

        if (!File.Exists(DatabasePaths.SettingsFilePath) && File.Exists(sourceSettingsPath))
        {
            File.Copy(sourceSettingsPath, DatabasePaths.SettingsFilePath, overwrite: false);
        }

        AppErrorLogger.LogInfo($"Legacy data migration completed. Backup: {backupPath}");
        return LegacyMigrationResult.Migrated(backupPath);
    }

    private static async Task<bool> CanOpenSqliteDatabaseAsync(string path)
    {
        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                ForeignKeys = true
            }.ToString();

            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM sqlite_master;";
            await command.ExecuteScalarAsync();
            return true;
        }
        catch (Exception ex)
        {
            AppErrorLogger.Log(ex, $"Legacy database validation failed: {path}");
            return false;
        }
    }
}

public sealed record LegacyMigrationResult(bool WasMigrated, bool Success, string Message, string? BackupPath)
{
    public static LegacyMigrationResult Skipped(string message)
    {
        return new LegacyMigrationResult(false, true, message, null);
    }

    public static LegacyMigrationResult Migrated(string backupPath)
    {
        return new LegacyMigrationResult(true, true, "Legacy data was copied to ProgramData.", backupPath);
    }

    public static LegacyMigrationResult Failed(string message)
    {
        return new LegacyMigrationResult(false, false, message, null);
    }
}
