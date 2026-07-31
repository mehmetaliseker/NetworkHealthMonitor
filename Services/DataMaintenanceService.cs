using System.Globalization;
using System.IO;
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
        await VerifyReadableSqliteDatabaseAsync(destinationPath, verifySchema: true);
    }

    public async Task<string> RestoreDatabaseAsync(string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Geri yüklenecek veritabanı bulunamadı.", sourcePath);
        }

        try
        {
            await VerifyReadableSqliteDatabaseAsync(sourcePath, verifySchema: true);
        }
        catch (Exception ex) when (ex is not FileNotFoundException)
        {
            throw new InvalidOperationException("Seçilen yedek geçerli bir NetworkHealthMonitor veritabanı değil.", ex);
        }
        await _connectionFactory.CheckpointAsync();
        SqliteConnection.ClearAllPools();

        Directory.CreateDirectory(DatabasePaths.DataDirectory);
        Directory.CreateDirectory(DatabasePaths.BackupDirectory);
        var automaticBackupPath = Path.Combine(
            DatabasePaths.BackupDirectory,
            $"network_health_monitor-before-restore-{DateTime.Now:yyyyMMdd-HHmmss}.db");
        var restoreTempPath = Path.Combine(
            DatabasePaths.DataDirectory,
            $"network_health_monitor-restore-{Guid.NewGuid():N}.db");

        try
        {
            if (File.Exists(DatabasePaths.DatabaseFilePath))
            {
                File.Copy(DatabasePaths.DatabaseFilePath, automaticBackupPath, overwrite: true);
            }

            File.Copy(sourcePath, restoreTempPath, overwrite: true);
            DeleteSqliteSidecars(DatabasePaths.DatabaseFilePath);
            File.Copy(restoreTempPath, DatabasePaths.DatabaseFilePath, overwrite: true);
            DeleteSqliteSidecars(DatabasePaths.DatabaseFilePath);

            await _connectionFactory.InitializeAsync();
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            await DatabaseSchemaContract.VerifyAsync(connection);
            return automaticBackupPath;
        }
        catch (Exception ex)
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(automaticBackupPath))
            {
                DeleteSqliteSidecars(DatabasePaths.DatabaseFilePath);
                File.Copy(automaticBackupPath, DatabasePaths.DatabaseFilePath, overwrite: true);
                DeleteSqliteSidecars(DatabasePaths.DatabaseFilePath);
                await _connectionFactory.InitializeAsync();
            }

            throw new InvalidOperationException("Veritabanı geri yüklenemedi; mevcut veritabanı korundu.", ex);
        }
        finally
        {
            if (File.Exists(restoreTempPath))
            {
                File.Delete(restoreTempPath);
            }
        }
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

    private static async Task VerifyReadableSqliteDatabaseAsync(string databasePath, bool verifySchema)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using (var integrity = connection.CreateCommand())
        {
            integrity.CommandText = "PRAGMA integrity_check;";
            var result = Convert.ToString(await integrity.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Seçilen veritabanı tutarlı değil: {result}");
            }
        }

        await using var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = "PRAGMA foreign_key_check;";
        await using var reader = await foreignKeys.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            throw new InvalidOperationException($"Seçilen veritabanında foreign key hatası var: {reader.GetString(0)} rowid {reader.GetInt64(1)}");
        }

        if (verifySchema)
        {
            await DatabaseSchemaContract.VerifyAsync(connection);
        }
    }

    private static void DeleteSqliteSidecars(string databasePath)
    {
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var path = databasePath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
