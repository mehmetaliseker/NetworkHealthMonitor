using System.IO;
using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class DeviceImportExportService
{
    private readonly CsvExportService _csvExportService;
    private readonly DeviceRepository? _deviceRepository;
    private readonly DataMaintenanceService? _maintenanceService;

    public DeviceImportExportService(
        CsvExportService csvExportService,
        DeviceRepository? deviceRepository = null,
        DataMaintenanceService? maintenanceService = null)
    {
        _csvExportService = csvExportService;
        _deviceRepository = deviceRepository;
        _maintenanceService = maintenanceService;
    }

    public Task ExportDevicesAsync(IEnumerable<Device> devices, string filePath, string delimiter = AppSettings.DefaultCsvDelimiter)
    {
        return _csvExportService.ExportDevicesAsync(devices, filePath, delimiter);
    }

    public Task ExportTemplateAsync(string filePath, string delimiter = AppSettings.DefaultCsvDelimiter)
    {
        return _csvExportService.ExportDeviceTemplateAsync(filePath, delimiter);
    }

    public Task<CsvImportPreview> ReadImportPreviewAsync(
        string filePath,
        IEnumerable<Device> existingDevices,
        string delimiter = AppSettings.DefaultCsvDelimiter)
    {
        return _csvExportService.ReadDeviceImportPreviewAsync(filePath, existingDevices, delimiter);
    }

    public Task<CsvImportPreview> ReadImportPreviewAsync(
        string filePath,
        IEnumerable<Device> existingDevices,
        CsvImportOptions options,
        string delimiter = AppSettings.DefaultCsvDelimiter)
    {
        return _csvExportService.ReadDeviceImportPreviewAsync(filePath, existingDevices, options, delimiter);
    }

    public async Task<CsvImportApplyResult> ApplyImportAsync(CsvImportPreview preview, CsvImportOptions options)
    {
        if (_deviceRepository is null || _maintenanceService is null)
        {
            throw new InvalidOperationException("CSV import apply requires a device repository and maintenance service.");
        }

        if (preview.HasBlockingErrors)
        {
            return await _deviceRepository.ApplyCsvImportAsync(preview, options, string.Empty);
        }

        var backupPath = Path.Combine(
            DatabasePaths.BackupDirectory,
            $"network_health_monitor-before-csv-import-{DateTime.Now:yyyyMMdd-HHmmss}.db");
        await _maintenanceService.BackupDatabaseAsync(backupPath);
        return await _deviceRepository.ApplyCsvImportAsync(preview, options, backupPath);
    }
}
