using System.IO;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.ViewModels;

public sealed partial class MainViewModel
{
    private async Task ExportDevicesAsync()
    {
        var path = _dialogService.GetSaveCsvFilePath($"cihaz-listesi-{DateTime.Now:yyyy-MM-dd}.csv", ExportDirectory);
        if (path is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _deviceImportExportService.ExportDevicesAsync(DevicesView.Cast<Device>().ToList(), path, CsvDelimiter);
            await RememberExportDirectoryAsync(path);
            StatusMessage = $"Cihaz listesi CSV olarak dışa aktarıldı: {path}";
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("Cihaz CSV oluşturulamadı", $"CSV dosyası oluşturulamadı. Seçilen klasöre yazma izniniz olmayabilir.\n\n{ex.Message}");
            StatusMessage = "Cihaz CSV dışa aktarılamadı.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CreateCsvTemplateAsync()
    {
        var path = _dialogService.GetSaveCsvFilePath($"cihaz-import-sablonu-{DateTime.Now:yyyy-MM-dd}.csv", ExportDirectory);
        if (path is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _deviceImportExportService.ExportTemplateAsync(path, CsvDelimiter);
            await RememberExportDirectoryAsync(path);
            StatusMessage = $"CSV import şablonu oluşturuldu: {path}";
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("CSV şablonu oluşturulamadı", $"CSV dosyası oluşturulamadı. Seçilen klasöre yazma izniniz olmayabilir.\n\n{ex.Message}");
            StatusMessage = "CSV import şablonu oluşturulamadı.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ImportDevicesAsync()
    {
        var path = _dialogService.GetOpenCsvFilePath();
        if (path is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var preview = await _deviceImportExportService.ReadImportPreviewAsync(path, Devices, CsvDelimiter);
            if (!preview.HasImportableRows)
            {
                _dialogService.ShowWarning("Import yapılamadı", "CSV içinde eklenebilir geçerli cihaz satırı bulunamadı.");
                return;
            }

            var duplicateAction = preview.ExistingIpCount > 0
                ? _dialogService.ChooseDuplicateImportAction("Duplicate IP bulundu", $"{preview.ExistingIpCount} IP adresi veritabanında zaten var.")
                : CsvImportDuplicateAction.UpdateExisting;

            if (duplicateAction == CsvImportDuplicateAction.Cancel)
            {
                StatusMessage = "CSV import iptal edildi.";
                return;
            }

            var result = await _deviceRepository.ImportDevicesAsync(preview.ValidRows, duplicateAction, preview.InvalidRowCount);
            await ReloadAllAsync();
            StatusMessage = $"CSV import tamamlandı. Eklenen: {result.Added}, güncellenen: {result.Updated}, atlanan: {result.Skipped}, hatalı: {result.Invalid}.";
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("CSV okunamadı", $"CSV dosyası okunamadı. Dosya kullanımda, hatalı formatta veya erişim izni kısıtlı olabilir.\n\n{ex.Message}");
            StatusMessage = "CSV import tamamlanamadı.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportLogsAsync()
    {
        var path = _dialogService.GetSaveCsvFilePath($"ping-loglari-{DateTime.Now:yyyy-MM-dd}.csv", ExportDirectory);
        if (path is null)
        {
            return;
        }

        await _csvExportService.ExportLogsAsync(Logs.ToList(), path, CsvDelimiter);
        await RememberExportDirectoryAsync(path);
        StatusMessage = $"Ping logları CSV olarak dışa aktarıldı: {path}";
    }

    private async Task ExportAvailabilityAsync()
    {
        var path = _dialogService.GetSaveCsvFilePath($"NetworkHealthMonitor_UptimeReport_{DateTime.Now:yyyyMMdd_HHmmss}.csv", ExportDirectory);
        if (path is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var items = await _pingLogRepository.GetUptimeReportAsync();
            await _csvExportService.ExportUptimeReportAsync(items, path, CsvDelimiter);
            await RememberExportDirectoryAsync(path);
            StatusMessage = $"Uptime CSV dışa aktarıldı: {path}";
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("Uptime CSV oluşturulamadı", ex.Message);
            StatusMessage = "Uptime CSV dışa aktarılamadı.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RememberExportDirectoryAsync(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        ExportDirectory = directory;
        var settings = await _settingsService.LoadAsync();
        settings.ExportDirectory = directory;
        await _settingsService.SaveAsync(settings);
    }
}

