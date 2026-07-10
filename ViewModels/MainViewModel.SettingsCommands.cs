using System.IO;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.ViewModels;

public sealed partial class MainViewModel
{
    private async Task ClearLogsAsync()
    {
        if (!_dialogService.Confirm("Tüm loglar temizlensin mi?", "Bu işlem tüm ping loglarını siler. Cihaz kayıtları korunur."))
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _pingLogRepository.ClearAsync();
            await ReloadAllAsync();
            StatusMessage = "Tüm ping logları temizlendi.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ClearOldLogsAsync()
    {
        if (LogRetentionDays <= 0)
        {
            _dialogService.ShowWarning("Geçersiz süre", "Log saklama süresi 0'dan büyük olmalıdır.");
            return;
        }

        if (!_dialogService.Confirm("Eski loglar temizlensin mi?", $"{LogRetentionDays} günden eski ping logları silinecek."))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var deleted = await _pingLogRepository.ClearOlderThanAsync(DateTime.Now.AddDays(-LogRetentionDays));
            if (deleted > 0)
            {
                await _maintenanceService.OptimizeDatabaseAsync();
            }

            await ReloadAllAsync();
            StatusMessage = $"{deleted} eski log temizlendi.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveSettingsAsync()
    {
        if (PingTimeoutMs < AppSettings.MinPingTimeoutMs || PingTimeoutMs > AppSettings.MaxPingTimeoutMs)
        {
            _dialogService.ShowWarning("Geçersiz ayar", $"Ping timeout değeri {AppSettings.MinPingTimeoutMs} ile {AppSettings.MaxPingTimeoutMs} ms arasında olmalıdır.");
            return;
        }

        if (MaxParallelPings < AppSettings.MinParallelPings || MaxParallelPings > AppSettings.MaxParallelPingsLimit)
        {
            _dialogService.ShowWarning("Geçersiz ayar", $"Maksimum paralel ping sayısı {AppSettings.MinParallelPings} ile {AppSettings.MaxParallelPingsLimit} arasında olmalıdır.");
            return;
        }

        if (DefaultFailureThreshold < AppSettings.MinFailureThreshold || DefaultFailureThreshold > AppSettings.MaxFailureThreshold)
        {
            _dialogService.ShowWarning("Geçersiz ayar", $"Varsayılan başarısızlık eşiği {AppSettings.MinFailureThreshold} ile {AppSettings.MaxFailureThreshold} arasında olmalıdır.");
            return;
        }

        if (AutoCheckIntervalMinutes < AppSettings.MinAutoCheckIntervalMinutes)
        {
            _dialogService.ShowWarning("Geçersiz ayar", $"Global otomatik kontrol aralığı en az {AppSettings.MinAutoCheckIntervalMinutes} dakika olmalıdır.");
            return;
        }

        if (SchedulerPollIntervalSeconds < AppSettings.MinSchedulerPollIntervalSeconds || SchedulerPollIntervalSeconds > AppSettings.MaxSchedulerPollIntervalSeconds)
        {
            _dialogService.ShowWarning("Geçersiz ayar", $"Scheduler kontrol sıklığı {AppSettings.MinSchedulerPollIntervalSeconds} ile {AppSettings.MaxSchedulerPollIntervalSeconds} saniye arasında olmalıdır.");
            return;
        }

        if (DefaultFailureRetryIntervalSeconds < AppSettings.MinFailureRetryIntervalSeconds || DefaultFailureRetryIntervalSeconds > AppSettings.MaxFailureRetryIntervalSeconds)
        {
            _dialogService.ShowWarning("Geçersiz ayar", $"Varsayılan hızlı tekrar aralığı {AppSettings.MinFailureRetryIntervalSeconds} saniye ile {AppSettings.MaxFailureRetryIntervalSeconds} saniye arasında olmalıdır.");
            return;
        }

        if (DefaultFailureRetryLimit < AppSettings.MinFailureRetryLimit || DefaultFailureRetryLimit > AppSettings.MaxFailureRetryLimit)
        {
            _dialogService.ShowWarning("Geçersiz ayar", $"Varsayılan hızlı tekrar limiti {AppSettings.MinFailureRetryLimit} ile {AppSettings.MaxFailureRetryLimit} arasında olmalıdır.");
            return;
        }

        if (LogRetentionDays < 0)
        {
            _dialogService.ShowWarning("Geçersiz ayar", "Log saklama süresi 0 veya daha büyük olmalıdır.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(ExportDirectory))
        {
            try
            {
                Directory.CreateDirectory(ExportDirectory);
            }
            catch (Exception ex)
            {
                _dialogService.ShowWarning("Export klasörü kullanılamıyor", ex.Message);
                return;
            }
        }

        await _settingsService.SaveAsync(CreateSettingsFromCurrentValues());

        StatusMessage = "Ayarlar kaydedildi.";
    }

    private async Task ResetSettingsAsync()
    {
        if (!_dialogService.Confirm("Ayarlar sıfırlansın mı?", "Genel uygulama ayarları varsayılan değerlere dönecek."))
        {
            return;
        }

        var defaults = AppSettings.Default;
        await _settingsService.SaveAsync(defaults);
        ApplySettings(defaults);
        StatusMessage = "Ayarlar sıfırlandı.";
    }

    private async Task BackupDatabaseAsync()
    {
        var path = _dialogService.GetSaveDatabaseFilePath($"network-health-monitor-{DateTime.Now:yyyyMMdd-HHmm}.db");
        if (path is null)
        {
            return;
        }

        await _maintenanceService.BackupDatabaseAsync(path);
        StatusMessage = "Veritabanı yedeklendi.";
    }

    private async Task RestoreDatabaseAsync()
    {
        var path = _dialogService.GetOpenDatabaseFilePath();
        if (path is null)
        {
            return;
        }

        if (!_dialogService.Confirm("Veritabanı geri yüklensin mi?", "Mevcut veritabanı otomatik yedeklenip seçilen dosya geri yüklenecek."))
        {
            return;
        }

        await StopSchedulerAsync();
        var backupPath = await _maintenanceService.RestoreDatabaseAsync(path);
        await ReloadAllAsync();
        StatusMessage = $"Veritabanı geri yüklendi. Önceki dosya yedeği: {backupPath}";
    }

    private async Task OptimizeDatabaseAsync()
    {
        IsBusy = true;
        try
        {
            await _maintenanceService.OptimizeDatabaseAsync();
            StatusMessage = "SQLite optimize işlemi tamamlandı.";
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("Veritabanı optimize edilemedi", ex.Message);
            StatusMessage = "SQLite optimize işlemi tamamlanamadı.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportSettingsAsync()
    {
        var path = _dialogService.GetSaveJsonFilePath($"network-health-monitor-settings-{DateTime.Now:yyyyMMdd-HHmm}.json");
        if (path is null)
        {
            return;
        }

        await SaveSettingsAsync();
        await _maintenanceService.ExportSettingsAsync(path);
        StatusMessage = "Ayarlar dışa aktarıldı.";
    }

    private async Task ImportSettingsAsync()
    {
        var path = _dialogService.GetOpenJsonFilePath();
        if (path is null)
        {
            return;
        }

        await _maintenanceService.ImportSettingsAsync(path);
        var settings = await _settingsService.LoadAsync();
        if (_settingsService.LastLoadUsedDefaultsDueToError)
        {
            _dialogService.ShowWarning("Ayar dosyası okunamadı", "İçe aktarılan ayar dosyası okunamadı; varsayılan ayarlar yüklendi.");
        }

        ApplySettings(settings);
        StatusMessage = "Ayarlar içe aktarıldı.";
    }

    private AppSettings CreateSettingsFromCurrentValues()
    {
        return new AppSettings
        {
            PingTimeoutMs = PingTimeoutMs,
            MaxParallelPings = MaxParallelPings,
            DefaultFailureThreshold = DefaultFailureThreshold,
            AutoCheckIntervalMinutes = AutoCheckIntervalMinutes,
            SchedulerPollIntervalSeconds = SchedulerPollIntervalSeconds,
            AutoCheckEnabled = AutoCheckEnabled,
            DefaultFailureRetryIntervalSeconds = DefaultFailureRetryIntervalSeconds,
            DefaultFailureRetryLimit = DefaultFailureRetryLimit,
            StartSchedulePlansOnStartup = StartSchedulePlansOnStartup,
            CsvDelimiter = CsvDelimiter,
            LogRetentionDays = LogRetentionDays,
            ExportDirectory = ExportDirectory,
            DeviceTypePolicies = DeviceTypePolicies.Select(policy => new DeviceTypePolicy
            {
                DeviceType = policy.DeviceType,
                AutoCheckEnabled = policy.AutoCheckEnabled,
                DefaultCheckIntervalSeconds = policy.DefaultCheckIntervalSeconds,
                DefaultPingTimeoutMs = policy.DefaultPingTimeoutMs,
                DefaultFailureRetryIntervalSeconds = policy.DefaultFailureRetryIntervalSeconds,
                DefaultFailureRetryLimit = policy.DefaultFailureRetryLimit,
                DefaultFailureThreshold = policy.DefaultFailureThreshold
            }).ToList(),
            Theme = Theme
        };
    }

    private async Task RunStartupRetentionCleanupAsync(AppSettings settings)
    {
        if (settings.LogRetentionDays <= 0)
        {
            return;
        }

        var deleted = await _pingLogRepository.ClearOlderThanAsync(DateTime.Now.AddDays(-settings.LogRetentionDays));
        if (deleted > 0)
        {
            await _maintenanceService.OptimizeDatabaseAsync();
        }
    }

    private void ApplySettings(AppSettings settings)
    {
        PingTimeoutMs = settings.PingTimeoutMs;
        MaxParallelPings = settings.MaxParallelPings;
        DefaultFailureThreshold = settings.DefaultFailureThreshold;
        AutoCheckIntervalMinutes = settings.AutoCheckIntervalMinutes;
        SchedulerPollIntervalSeconds = settings.SchedulerPollIntervalSeconds;
        AutoCheckEnabled = settings.AutoCheckEnabled;
        DefaultFailureRetryIntervalSeconds = settings.DefaultFailureRetryIntervalSeconds;
        DefaultFailureRetryLimit = settings.DefaultFailureRetryLimit;
        StartSchedulePlansOnStartup = settings.StartSchedulePlansOnStartup;
        CsvDelimiter = settings.CsvDelimiter;
        LogRetentionDays = settings.LogRetentionDays;
        ExportDirectory = settings.ExportDirectory;
        ReplaceCollection(DeviceTypePolicies, DeviceTypePolicy.NormalizeCollection(settings.DeviceTypePolicies));
        Theme = settings.Theme;
        PlanFormTimeoutMs = settings.PingTimeoutMs;
        PlanFormMaxParallelism = Math.Min(settings.MaxParallelPings, AppSettings.DefaultSchedulePlanMaxParallelism);
        PlanFormFailureThreshold = settings.DefaultFailureThreshold;
        FormCheckIntervalSeconds = 0;
        FormFailureRetryIntervalSeconds = 0;
        FormFailureRetryLimit = 0;
        FormFailureThreshold = 0;
    }
}

