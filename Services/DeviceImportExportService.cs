using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class DeviceImportExportService
{
    private readonly CsvExportService _csvExportService;

    public DeviceImportExportService(CsvExportService csvExportService)
    {
        _csvExportService = csvExportService;
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
}
