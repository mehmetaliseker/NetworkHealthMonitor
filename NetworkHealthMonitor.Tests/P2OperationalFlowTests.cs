using System.Text;
using Microsoft.Data.Sqlite;
using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Models;
using NetworkHealthMonitor.Services;
using Xunit;

namespace NetworkHealthMonitor.Tests;

public sealed class P2OperationalFlowTests
{
    [Fact]
    public async Task Backup_restore_verify_and_corrupt_restore_rollback_preserve_current_database()
    {
        await using var store = await TestStore.CreateAsync();
        var repository = new DeviceRepository(store.ConnectionFactory);
        var deviceService = new DeviceService(repository);
        var maintenance = new DataMaintenanceService(store.ConnectionFactory);
        var backupPath = Path.Combine(store.Root, "backups", "NetworkHealthMonitor-20260731-120000.db");
        var corruptPath = Path.Combine(store.Root, "backups", "corrupt.db");

        var original = await SaveDeviceAsync(deviceService, "NHR-P2-BACKUP-ORIGINAL", "192.0.2.80");
        await maintenance.BackupDatabaseAsync(backupPath);

        Assert.True(File.Exists(backupPath));
        Assert.True(new FileInfo(backupPath).Length > 0);
        await VerifyDatabaseFileAsync(backupPath);

        await SaveDeviceAsync(deviceService, "NHR-P2-BACKUP-AFTER", "192.0.2.81");
        Assert.Equal(2, await CountDevicesAsync(store));

        var safetyBackupPath = await maintenance.RestoreDatabaseAsync(backupPath);
        Assert.True(File.Exists(safetyBackupPath));

        var afterRestore = await repository.GetAllAsync();
        Assert.Contains(afterRestore, device => device.Id == original.Id && device.Name == original.Name);
        Assert.DoesNotContain(afterRestore, device => device.Name == "NHR-P2-BACKUP-AFTER");

        var protectedDevice = await SaveDeviceAsync(deviceService, "NHR-P2-BACKUP-PRESERVED", "192.0.2.82");
        await File.WriteAllTextAsync(corruptPath, "not a sqlite database", Encoding.UTF8);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => maintenance.RestoreDatabaseAsync(corruptPath));
        Assert.Contains("NetworkHealthMonitor", exception.Message, StringComparison.OrdinalIgnoreCase);

        var afterCorruptRestore = await repository.GetAllAsync();
        Assert.Contains(afterCorruptRestore, device => device.Id == protectedDevice.Id && device.Name == protectedDevice.Name);
    }

    [Fact]
    public async Task Csv_import_export_reports_bad_rows_preserves_utf8_and_escapes_spreadsheet_formulas()
    {
        await using var store = await TestStore.CreateAsync();
        var repository = new DeviceRepository(store.ConnectionFactory);
        var maintenance = new DataMaintenanceService(store.ConnectionFactory);
        var csv = new CsvExportService();
        var importExport = new DeviceImportExportService(csv, repository, maintenance);
        var options = new CsvImportOptions(
            CsvImportMode.Upsert,
            CsvImportScope.AllActiveDevices,
            null,
            string.Empty,
            "p2-devices.csv",
            "p2-test");
        var invalidCsvPath = Path.Combine(store.Root, "invalid-devices.csv");
        var validCsvPath = Path.Combine(store.Root, "valid-devices.csv");
        var errorReportPath = Path.Combine(store.Root, "csv-errors.csv");
        var exportPath = Path.Combine(store.Root, "devices-export.csv");

        await File.WriteAllTextAsync(
            invalidCsvPath,
            "Name;IpAddress;DeviceType;GroupName;Description\nBad;999.999.999.999;Server;P2;Bad address\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var invalidPreview = await importExport.ReadImportPreviewAsync(invalidCsvPath, await repository.GetAllAsync(includeDeleted: true), options);
        Assert.Equal(1, invalidPreview.InvalidRowCount);
        await csv.ExportImportErrorsAsync(invalidPreview.Errors, errorReportPath, ";");
        Assert.Contains("999.999.999.999", await File.ReadAllTextAsync(errorReportPath, Encoding.UTF8));

        await File.WriteAllTextAsync(
            validCsvPath,
            "Name;IpAddress;DeviceType;GroupName;Description\nNHR-P2-Üzüm;192.0.2.91;Server;Üzüm İşletmesi;=SUM(1,1)\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var preview = await importExport.ReadImportPreviewAsync(validCsvPath, await repository.GetAllAsync(includeDeleted: true), options);
        Assert.False(preview.HasBlockingErrors);
        Assert.Equal(1, preview.AddCount);

        var result = await importExport.ApplyImportAsync(preview, options);
        Assert.Equal(1, result.Added);
        Assert.Equal(0, result.Invalid);

        await importExport.ExportDevicesAsync(await repository.GetAllAsync(), exportPath, ";");
        var bytes = await File.ReadAllBytesAsync(exportPath);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        var exported = await File.ReadAllTextAsync(exportPath, Encoding.UTF8);
        Assert.Contains("NHR-P2-Üzüm", exported);
        Assert.Contains("Üzüm İşletmesi", exported);
        Assert.Contains("'=SUM(1,1)", exported);
    }

    [Fact]
    public async Task Uptime_report_csv_uses_real_sqlite_ping_data()
    {
        await using var store = await TestStore.CreateAsync();
        var deviceRepository = new DeviceRepository(store.ConnectionFactory);
        var deviceService = new DeviceService(deviceRepository);
        var pingLogRepository = new PingLogRepository(store.ConnectionFactory);
        var csv = new CsvExportService();
        var reportPath = Path.Combine(store.Root, "uptime-report.csv");
        var device = await SaveDeviceAsync(deviceService, "NHR-P2-REPORT", "192.0.2.92");

        await pingLogRepository.AddRangeAsync(new[]
        {
            CreatePingLog(device, DeviceStatus.Online, true, 3, DateTime.Now.AddMinutes(-10)),
            CreatePingLog(device, DeviceStatus.Offline, false, null, DateTime.Now.AddMinutes(-5))
        });

        var report = await pingLogRepository.GetUptimeReportAsync(new[] { device.Id });
        var item = Assert.Single(report);
        Assert.Equal(2, item.TotalChecksOverall);
        Assert.Equal(1, item.SuccessfulChecksOverall);
        Assert.Equal(1, item.FailedChecksOverall);

        await csv.ExportUptimeReportAsync(report, reportPath, ";");
        var exported = await File.ReadAllTextAsync(reportPath, Encoding.UTF8);
        Assert.Contains("NHR-P2-REPORT", exported);
        Assert.Contains("UptimeOverallPercent", exported);
    }

    [Fact]
    public async Task Settings_persist_operational_values_without_enabling_windows_autostart_flags()
    {
        await using var store = await TestStore.CreateAsync();
        var settingsService = new AppSettingsService();
        var settings = await settingsService.LoadAsync();
        settings.PingTimeoutMs = 2500;
        settings.MaxParallelPings = 7;
        settings.DefaultFailureRetryIntervalSeconds = 45;
        settings.AvailabilityPeriodRetentionDays = 365;
        settings.IncidentRetentionDays = 730;
        settings.Theme = "Açık";
        settings.OpenUiOnWindowsLogin = false;

        await settingsService.SaveAsync(settings);
        var reloaded = await settingsService.LoadAsync();

        Assert.Equal(2500, reloaded.PingTimeoutMs);
        Assert.Equal(7, reloaded.MaxParallelPings);
        Assert.Equal(45, reloaded.DefaultFailureRetryIntervalSeconds);
        Assert.Equal(365, reloaded.AvailabilityPeriodRetentionDays);
        Assert.Equal(730, reloaded.IncidentRetentionDays);
        Assert.Equal("Açık", reloaded.Theme);
        Assert.False(reloaded.OpenUiOnWindowsLogin);
    }

    private static async Task<Device> SaveDeviceAsync(DeviceService service, string name, string address)
    {
        var device = new Device
        {
            Name = name,
            IpAddress = address,
            DeviceType = DeviceType.Server,
            GroupName = "NHR-P2",
            IsActive = true,
            IsEnabled = true,
            AutoCheckEnabled = true
        };
        var result = await service.SaveAsync(device);
        Assert.True(result.Success, result.Message);
        return device;
    }

    private static PingLog CreatePingLog(Device device, DeviceStatus status, bool isReachable, long? latencyMs, DateTime checkedAt)
    {
        return new PingLog
        {
            DeviceId = device.Id,
            DeviceName = device.Name,
            IpAddress = device.IpAddress,
            DeviceType = device.DeviceType,
            GroupName = device.GroupName,
            Status = status,
            IsReachable = isReachable,
            LatencyMs = latencyMs,
            ResponseMessage = isReachable ? "Pong" : string.Empty,
            ErrorCode = isReachable ? string.Empty : "Timeout",
            ErrorMessage = isReachable ? string.Empty : "No reply",
            CheckedAt = checkedAt,
            Source = "P2Test",
            TriggerType = PingTriggerType.Manual,
            WorkerInstanceId = "p2-test"
        };
    }

    private static async Task<int> CountDevicesAsync(TestStore store)
    {
        await using var connection = await store.ConnectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM Devices WHERE IsDeleted = 0;";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task VerifyDatabaseFileAsync(string path)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await DatabaseSchemaContract.VerifyAsync(connection);
    }
}
