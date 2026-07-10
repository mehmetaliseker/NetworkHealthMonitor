using System.IO;
using Microsoft.Data.Sqlite;
using NetworkHealthMonitor.Data;

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

        Directory.CreateDirectory(DatabasePaths.AppDataDirectory);
        var automaticBackupPath = Path.Combine(
            DatabasePaths.AppDataDirectory,
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
}
