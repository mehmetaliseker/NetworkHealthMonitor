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
        var path = _dialogService.GetSaveCsvFilePath($"cihaz-ice-aktarma-sablonu-{DateTime.Now:yyyy-MM-dd}.csv", ExportDirectory);
        if (path is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _deviceImportExportService.ExportTemplateAsync(path, CsvDelimiter);
            await RememberExportDirectoryAsync(path);
            StatusMessage = $"CSV içe aktarma şablonu oluşturuldu: {path}";
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("CSV şablonu oluşturulamadı", $"CSV dosyası oluşturulamadı. Seçilen klasöre yazma izniniz olmayabilir.\n\n{ex.Message}");
            StatusMessage = "CSV içe aktarma şablonu oluşturulamadı.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PreviewImportDevicesAsync()
    {
        var path = _dialogService.GetOpenCsvFilePath();
        if (path is null)
        {
            return;
        }

        if (CsvImportMode == NetworkHealthMonitor.Models.CsvImportMode.Sync
            && CsvImportScope == NetworkHealthMonitor.Models.CsvImportScope.SelectedGroup
            && !CsvImportGroupId.HasValue)
        {
            _dialogService.ShowWarning("Grup seçilmedi", "Grup kapsamlı CSV eşitleme için önce bir grup seçin.");
            return;
        }

        IsBusy = true;
        try
        {
            var options = CreateCsvImportOptions(path);
            var preview = await _deviceImportExportService.ReadImportPreviewAsync(path, Devices, options, CsvDelimiter);
            ApplyCsvPreview(path, preview);
            StatusMessage = preview.HasBlockingErrors
                ? "CSV önizleme tamamlandı; hatalar düzeltilmeden içe aktarma uygulanamaz."
                : "CSV önizleme tamamlandı. Özet ve ayrıntı sekmelerini kontrol edin.";
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("CSV okunamadı", $"CSV dosyası okunamadı. Dosya kullanımda, hatalı formatta veya erişim izni kısıtlı olabilir.\n\n{ex.Message}");
            StatusMessage = "CSV içe aktarma tamamlanamadı.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyCsvImportAsync()
    {
        if (_csvImportPreview is null)
        {
            _dialogService.ShowWarning("Önizleme gerekli", "İçe aktarmayı uygulamadan önce CSV önizlemesi alın.");
            return;
        }

        if (!_csvImportPreview.HasImportableRows)
        {
            _dialogService.ShowWarning("İçe aktarma uygulanamaz", "CSV önizlemesinde bloklayıcı hata var veya geçerli satır yok.");
            return;
        }

        if (!_dialogService.Confirm(
                "CSV içe aktarma uygulansın mı?",
                _csvImportPreview.SummaryText + "\n\nCSV ile tamamen eşitle modunda CSV'de olmayan cihazlar soft-delete yapılır; ping ve kesinti geçmişi korunur."))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _deviceImportExportService.ApplyImportAsync(_csvImportPreview, CreateCsvImportOptions(CsvImportFilePath));
            ClearCsvPreview();
            await ReloadAllAsync();
            StatusMessage = $"CSV içe aktarma tamamlandı. Eklenen: {result.Added}, güncellenen: {result.Updated}, silinen: {result.Deleted}, geri yüklenen: {result.Restored}, atlanan: {result.Skipped}. Yedek: {result.BackupPath}";
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("CSV içe aktarma uygulanamadı", ex.Message);
            StatusMessage = "CSV içe aktarma işlemi geri alındı.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private CsvImportOptions CreateCsvImportOptions(string filePath)
    {
        var groupName = CsvImportGroupId.HasValue
            ? DeviceGroups.FirstOrDefault(group => group.Id == CsvImportGroupId.Value)?.Name ?? string.Empty
            : string.Empty;
        return new CsvImportOptions(
            CsvImportMode,
            CsvImportScope,
            CsvImportGroupId,
            groupName,
            Path.GetFileName(filePath),
            $"{Environment.UserDomainName}\\{Environment.UserName}");
    }

    private void ApplyCsvPreview(string path, CsvImportPreview preview)
    {
        _csvImportPreview = preview;
        CsvImportFilePath = path;
        CsvImportPreviewSummary = preview.SummaryText;
        ReplaceCollection(CsvNewRows, preview.Rows.Where(row => row.Status == CsvImportRowStatus.Add || row.Status == CsvImportRowStatus.Restore));
        ReplaceCollection(CsvUpdateRows, preview.Rows.Where(row => row.Status == CsvImportRowStatus.Update));
        ReplaceCollection(CsvDeleteRows, preview.Rows.Where(row => row.Status == CsvImportRowStatus.Delete));
        ReplaceCollection(CsvInvalidRows, preview.Rows.Where(row => row.Status == CsvImportRowStatus.Invalid));
        ReplaceCollection(CsvDuplicateRows, preview.Rows.Where(row => row.Status == CsvImportRowStatus.Duplicate));
        ReplaceCollection(CsvUnchangedRows, preview.Rows.Where(row => row.Status == CsvImportRowStatus.Unchanged || row.Status == CsvImportRowStatus.Skip));
        OnPropertyChanged(nameof(CsvImportCanApply));
        ApplyCsvImportCommand.NotifyCanExecuteChanged();
    }

    private void ClearCsvPreview()
    {
        _csvImportPreview = null;
        CsvImportFilePath = string.Empty;
        CsvImportPreviewSummary = "CSV önizlemesi henüz alınmadı.";
        CsvNewRows.Clear();
        CsvUpdateRows.Clear();
        CsvDeleteRows.Clear();
        CsvInvalidRows.Clear();
        CsvDuplicateRows.Clear();
        CsvUnchangedRows.Clear();
        OnPropertyChanged(nameof(CsvImportCanApply));
        ApplyCsvImportCommand?.NotifyCanExecuteChanged();
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
        var path = _dialogService.GetSaveCsvFilePath($"NetworkHealthMonitor_ErisilebilirlikOzeti_{DateTime.Now:yyyyMMdd_HHmmss}.csv", ExportDirectory);
        if (path is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var endUtc = DateTime.UtcNow;
            var startUtc = endUtc.AddDays(-30);
            var items = await _availabilityService.GetAvailabilitySummaryAsync(startUtc, endUtc, TimeZoneInfo.Local.Id);
            await _csvExportService.ExportAvailabilitySummaryAsync(items, path, CsvDelimiter);
            await RememberExportDirectoryAsync(path);
            StatusMessage = $"Erişilebilirlik özeti CSV olarak dışa aktarıldı: {path}";
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("Erişilebilirlik CSV oluşturulamadı", ex.Message);
            StatusMessage = "Erişilebilirlik CSV dışa aktarılamadı.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportAvailabilityIncidentsAsync()
    {
        var path = _dialogService.GetSaveCsvFilePath($"NetworkHealthMonitor_Kesintiler_{DateTime.Now:yyyyMMdd_HHmmss}.csv", ExportDirectory);
        if (path is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var endUtc = DateTime.UtcNow;
            var startUtc = endUtc.AddDays(-30);
            var items = await _availabilityService.GetIncidentReportAsync(startUtc, endUtc);
            await _csvExportService.ExportAvailabilityIncidentsAsync(items, path, CsvDelimiter);
            await RememberExportDirectoryAsync(path);
            StatusMessage = $"Kesinti CSV'si dışa aktarıldı: {path}";
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("Kesinti CSV oluşturulamadı", ex.Message);
            StatusMessage = "Kesinti CSV dışa aktarılamadı.";
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
