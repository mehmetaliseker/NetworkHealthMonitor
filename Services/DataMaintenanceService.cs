using System.IO;
using System.Globalization;
using Microsoft.Data.Sqlite;
using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class DataMaintenanceService
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public DataMaintenanceService(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task BackupDatabaseAsync(string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? ".");
        await _connectionFactory.CheckpointAsync();
        SqliteConnection.ClearAllPools();
        File.Copy(DatabasePaths.DatabaseFilePath, destinationPath, overwrite: true);
    }

    public async Task<string> RestoreDatabaseAsync(string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Geri yüklenecek veritabanı bulunamadı.", sourcePath);
        }

        await _connectionFactory.CheckpointAsync();
        SqliteConnection.ClearAllPools();

        Directory.CreateDirectory(DatabasePaths.DataDirectory);
        var automaticBackupPath = Path.Combine(
            DatabasePaths.BackupDirectory,
            $"network_health_monitor-before-restore-{DateTime.Now:yyyyMMdd-HHmmss}.db");

        if (File.Exists(DatabasePaths.DatabaseFilePath))
        {
            File.Copy(DatabasePaths.DatabaseFilePath, automaticBackupPath, overwrite: true);
        }

        File.Copy(sourcePath, DatabasePaths.DatabaseFilePath, overwrite: true);
        await _connectionFactory.InitializeAsync();
        return automaticBackupPath;
    }

    public Task ExportSettingsAsync(string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? ".");
        File.Copy(DatabasePaths.SettingsFilePath, destinationPath, overwrite: true);
        return Task.CompletedTask;
    }

    public Task ImportSettingsAsync(string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("İçe aktarılacak ayar dosyası bulunamadı.", sourcePath);
        }

        Directory.CreateDirectory(DatabasePaths.AppDataDirectory);
        File.Copy(sourcePath, DatabasePaths.SettingsFilePath, overwrite: true);
        return Task.CompletedTask;
    }

    public async Task OptimizeDatabaseAsync()
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA optimize;";
        await command.ExecuteNonQueryAsync();
    }

    public async Task<int> ApplyRetentionAsync(AppSettings settings)
    {
        var deleted = 0;
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();

        if (settings.AvailabilityPeriodRetentionDays > 0)
        {
            deleted += await DeleteOlderThanAsync(
                connection,
                transaction,
                "DeviceAvailabilityPeriods",
                "COALESCE(EndedAtUtc, StartedAtUtc)",
                DateTime.UtcNow.AddDays(-settings.AvailabilityPeriodRetentionDays));
        }

        if (settings.IncidentRetentionDays > 0)
        {
            deleted += await DeleteOlderThanAsync(
                connection,
                transaction,
                "DeviceIncidents",
                "COALESCE(EndedAtUtc, StartedAtUtc)",
                DateTime.UtcNow.AddDays(-settings.IncidentRetentionDays));
        }

        if (settings.DailyAggregateRetentionDays > 0)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                DELETE FROM DeviceAvailabilityDaily
                WHERE Date < @ThresholdDate;
                """;
            command.Parameters.AddWithValue("@ThresholdDate", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-settings.DailyAggregateRetentionDays)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            deleted += await command.ExecuteNonQueryAsync();
        }

        transaction.Commit();
        return deleted;
    }

    private static async Task<int> DeleteOlderThanAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        string dateExpression,
        DateTime thresholdUtc)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            DELETE FROM {tableName}
            WHERE {dateExpression} < @ThresholdUtc
              AND NOT EXISTS (
                  SELECT 1
                  FROM DeviceAvailabilityPeriods p
                  WHERE p.Id = {tableName}.Id
                    AND p.EndedAtUtc IS NULL
              );
            """;
        if (!string.Equals(tableName, "DeviceAvailabilityPeriods", StringComparison.OrdinalIgnoreCase))
        {
            command.CommandText = $"""
                DELETE FROM {tableName}
                WHERE {dateExpression} < @ThresholdUtc;
                """;
        }

        command.Parameters.AddWithValue("@ThresholdUtc", thresholdUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        return await command.ExecuteNonQueryAsync();
    }
}
