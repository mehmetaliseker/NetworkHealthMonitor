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
            return LegacyMigrationResult.Skipped("ProgramData veritabanı zaten mevcut.");
        }

        if (!File.Exists(DatabasePaths.LegacyDatabaseFilePath))
        {
            return LegacyMigrationResult.Skipped("Taşınacak eski LocalAppData veritabanı bulunamadı.");
        }

        if (!await CanOpenSqliteDatabaseAsync(DatabasePaths.LegacyDatabaseFilePath))
        {
            return LegacyMigrationResult.Failed("Eski veritabanı SQLite olarak açılamadı.");
        }

        var backupPath = Path.Combine(
            DatabasePaths.BackupDirectory,
            $"legacy-localappdata-{DateTime.Now:yyyyMMdd-HHmmss}.db");

        File.Copy(DatabasePaths.LegacyDatabaseFilePath, backupPath, overwrite: false);
        File.Copy(DatabasePaths.LegacyDatabaseFilePath, DatabasePaths.DatabaseFilePath, overwrite: false);

        if (!await CanOpenSqliteDatabaseAsync(DatabasePaths.DatabaseFilePath))
        {
            File.Delete(DatabasePaths.DatabaseFilePath);
            return LegacyMigrationResult.Failed("Kopyalanan ProgramData veritabanı açılamadı.");
        }

        if (!File.Exists(DatabasePaths.SettingsFilePath) && File.Exists(DatabasePaths.LegacySettingsFilePath))
        {
            File.Copy(DatabasePaths.LegacySettingsFilePath, DatabasePaths.SettingsFilePath, overwrite: false);
        }

        var result = LegacyMigrationResult.Migrated(backupPath);
        AppErrorLogger.LogInfo($"Legacy data migration completed. Backup: {backupPath}");
        return result;
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
        return new LegacyMigrationResult(true, true, "Eski LocalAppData veritabanı ProgramData altına taşındı.", backupPath);
    }

    public static LegacyMigrationResult Failed(string message)
    {
        return new LegacyMigrationResult(false, false, message, null);
    }
}
