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
            _dialogService.ShowWarning("Geçersiz ayar", $"Ping zaman aşımı değeri {AppSettings.MinPingTimeoutMs} ile {AppSettings.MaxPingTimeoutMs} ms arasında olmalıdır.");
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
                _dialogService.ShowWarning("Dışa aktarma klasörü kullanılamıyor", ex.Message);
                return;
            }
        }

        if (!TryValidateEmailSettings(out var emailValidationMessage))
        {
            _dialogService.ShowWarning("E-posta ayarlari gecersiz", emailValidationMessage);
            return;
        }

        await _uiAutostartService.SetEnabledAsync(OpenUiOnWindowsLogin, ResolveCurrentExecutablePath());
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
            OpenUiOnWindowsLogin = OpenUiOnWindowsLogin,
            CsvDelimiter = CsvDelimiter,
            LogRetentionDays = LogRetentionDays,
            AvailabilityPeriodRetentionDays = AvailabilityPeriodRetentionDays,
            IncidentRetentionDays = IncidentRetentionDays,
            DailyAggregateRetentionDays = DailyAggregateRetentionDays,
            HeartbeatGraceSeconds = HeartbeatGraceSeconds,
            ExpectedCheckGraceMultiplier = ExpectedCheckGraceMultiplier,
            DowntimeStartPolicy = DowntimeStartPolicy,
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
            Notifications = new NotificationSettings
            {
                Enabled = NotificationEnabled,
                BaseUrl = NotificationBaseUrl,
                Topic = NotificationTopic,
                AccessToken = NotificationAccessToken,
                IncludeIpAddress = NotificationIncludeIpAddress,
                NotifyOnDeviceDown = NotificationNotifyOnDown,
                NotifyOnDeviceRecovered = NotificationNotifyOnRecovered,
                DownFailureThreshold = NotificationDownFailureThreshold,
                RecoverySuccessThreshold = NotificationRecoverySuccessThreshold,
                NotificationCooldownMinutes = NotificationCooldownMinutes,
                RequestTimeoutSeconds = NotificationRequestTimeoutSeconds,
                MaxRetryCount = NotificationMaxRetryCount,
                InitialRetryDelaySeconds = NotificationInitialRetryDelaySeconds,
                EmailEnabled = EmailEnabled,
                SmtpHost = SmtpHost,
                SmtpPort = SmtpPort,
                SmtpSecurity = SmtpSecurity,
                AllowInsecureSmtp = AllowInsecureSmtp,
                SmtpUsername = SmtpUsername,
                SmtpPassword = SmtpPassword,
                SenderEmail = SenderEmail,
                SenderDisplayName = SenderDisplayName,
                SmtpConnectionTimeoutSeconds = SmtpConnectionTimeoutSeconds,
                EmailMaxRetryCount = EmailMaxRetryCount,
                TestEmailRecipient = TestEmailRecipient,
                EscalationThresholdHours = EmailEscalationThresholdHours,
                NotifyOnDeviceEscalated = NotificationNotifyOnEscalated,
                EmailNotifyOnDeviceRecovered = EmailNotifyOnRecovered,
                InitialEmailRecipients = InitialEmailRecipients.Select(CloneRecipient).ToList(),
                EscalationEmailRecipients = EscalationEmailRecipients.Select(CloneRecipient).ToList(),
                EmailTemplates = new EmailTemplateSettings
                {
                    InitialOfflineSubject = InitialOfflineEmailSubjectTemplate,
                    InitialOfflineBody = InitialOfflineEmailBodyTemplate,
                    EscalationSubject = EscalationEmailSubjectTemplate,
                    EscalationBody = EscalationEmailBodyTemplate,
                    RecoveredSubject = RecoveredEmailSubjectTemplate,
                    RecoveredBody = RecoveredEmailBodyTemplate,
                    IsHtml = EmailTemplatesAreHtml
                }
            },
            Theme = Theme
        };
    }

    private static EmailRecipient CloneRecipient(EmailRecipient recipient)
    {
        return new EmailRecipient
        {
            Email = recipient.Email.Trim(),
            DisplayName = recipient.DisplayName.Trim()
        };
    }

    private bool TryValidateEmailSettings(out string message)
    {
        message = string.Empty;

        var renderer = new NetworkHealthMonitor.Services.NotificationTemplateRenderer();
        var unknownPlaceholders = new[]
            {
                InitialOfflineEmailSubjectTemplate,
                InitialOfflineEmailBodyTemplate,
                EscalationEmailSubjectTemplate,
                EscalationEmailBodyTemplate,
                RecoveredEmailSubjectTemplate,
                RecoveredEmailBodyTemplate
            }
            .SelectMany(renderer.FindUnknownPlaceholders)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (unknownPlaceholders.Count > 0)
        {
            message = "Bilinmeyen sablon degiskenleri: " + string.Join(", ", unknownPlaceholders.Select(item => "{" + item + "}"));
            return false;
        }

        if (string.IsNullOrWhiteSpace(InitialOfflineEmailSubjectTemplate)
            || string.IsNullOrWhiteSpace(EscalationEmailSubjectTemplate)
            || string.IsNullOrWhiteSpace(RecoveredEmailSubjectTemplate))
        {
            message = "E-posta konu sablonlari bos birakilamaz.";
            return false;
        }

        var invalidRecipients = InitialEmailRecipients
            .Concat(EscalationEmailRecipients)
            .Where(recipient => !string.IsNullOrWhiteSpace(recipient.Email) && !EmailAddressValidator.IsValid(recipient.Email))
            .Select(recipient => recipient.Email)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (invalidRecipients.Count > 0)
        {
            message = "Gecersiz alici adresleri: " + string.Join(", ", invalidRecipients);
            return false;
        }

        if (!EmailEnabled)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(SmtpHost))
        {
            message = "SMTP sunucu adresi bos olamaz.";
            return false;
        }

        if (!EmailAddressValidator.IsValid(SenderEmail))
        {
            message = "Gonderen e-posta adresi gecerli degil.";
            return false;
        }

        if (InitialEmailRecipients.All(recipient => !EmailAddressValidator.IsValid(recipient.Email)))
        {
            message = "Ilk bildirim icin en az bir gecerli alici ekleyin.";
            return false;
        }

        if (NotificationNotifyOnEscalated
            && EscalationEmailRecipients.All(recipient => !EmailAddressValidator.IsValid(recipient.Email)))
        {
            message = "Escalation bildirimi icin en az bir gecerli alici ekleyin.";
            return false;
        }

        if (SmtpSecurity == SmtpSecurityMode.None && !AllowInsecureSmtp)
        {
            message = "Guvenliksiz SMTP icin acik onay kutusunu isaretleyin.";
            return false;
        }

        return true;
    }

    private static string ResolveCurrentExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
        {
            return processPath;
        }

        var assemblyPath = System.Reflection.Assembly.GetEntryAssembly()?.Location;
        if (!string.IsNullOrWhiteSpace(assemblyPath) && File.Exists(assemblyPath))
        {
            var exePath = Path.ChangeExtension(assemblyPath, ".exe");
            return File.Exists(exePath) ? exePath : assemblyPath;
        }

        throw new InvalidOperationException("UI executable path could not be resolved.");
    }

    private async Task SendTestNotificationAsync()
    {
        var settings = CreateSettingsFromCurrentValues();
        settings.Notifications.Enabled = true;
        settings.Notifications.BaseUrl = settings.Notifications.BaseUrl.Trim();
        settings.Notifications.Topic = settings.Notifications.Topic.Trim();
        settings.Notifications.AccessToken = settings.Notifications.AccessToken.Trim();

        var topic = settings.Notifications.Topic;
        var payload = new NtfyNotificationPayload
        {
            EventType = "Test",
            Title = "NetworkHealthMonitor test bildirimi",
            Message = "Bildirim ayarlarınız başarıyla doğrulandı.",
            Priority = "default",
            Tags = "white_check_mark"
        };

        IsBusy = true;
        IsSendingTestNotification = true;
        NotificationLastTestResult = "Bildirim gönderiliyor…";
        NotificationLastTestTechnicalDetail = string.Empty;
        NotificationShowTechnicalDetail = false;
        try
        {
            var result = await _ntfyNotificationClient.PublishAsync(settings.Notifications, payload);
            if (result.Success)
            {
                NotificationLastTestResult =
                    $"Test bildirimi başarıyla gönderildi.{Environment.NewLine}" +
                    $"Telefonunuzdaki ntfy uygulamasında “{topic}” konusunu kontrol edin.";
                settings.Notifications.LastSuccessfulNotificationAtUtc = DateTime.UtcNow;
                settings.Notifications.LastNotificationError = string.Empty;
                NotificationLastError = string.Empty;
                NotificationLastTestTechnicalDetail = string.Empty;
            }
            else
            {
                NotificationLastTestResult = string.IsNullOrWhiteSpace(result.UserMessage)
                    ? result.SafeErrorMessage
                    : result.UserMessage;
                NotificationLastTestTechnicalDetail = result.TechnicalDetail;
                settings.Notifications.LastNotificationError = result.SafeErrorMessage;
                NotificationLastError = result.SafeErrorMessage;
            }

            settings.Notifications.LastTestAtUtc = DateTime.UtcNow;
            settings.Notifications.LastTestResult = NotificationLastTestResult;
            await _settingsService.SaveAsync(settings);
            ApplySettings(settings);
            NotificationLastTestResult = settings.Notifications.LastTestResult;
            if (!result.Success)
            {
                NotificationLastTestTechnicalDetail = result.TechnicalDetail;
            }
        }
        finally
        {
            IsSendingTestNotification = false;
            IsBusy = false;
        }
    }

    private async Task TestSmtpConnectionAsync()
    {
        var settings = CreateSettingsFromCurrentValues();
        IsBusy = true;
        IsSendingTestEmail = true;
        SmtpTestResult = "SMTP ayarlari dogrulaniyor...";
        try
        {
            var result = await _emailSender.TestConnectionAsync(settings.Notifications);
            SmtpTestResult = result.Success
                ? "SMTP ayarlari temel dogrulamadan gecti. Gercek teslimat icin test e-postasi gonderin."
                : result.SafeErrorMessage;
        }
        finally
        {
            IsSendingTestEmail = false;
            IsBusy = false;
        }
    }

    private async Task SendTestEmailAsync()
    {
        var settings = CreateSettingsFromCurrentValues();
        settings.Notifications.EmailEnabled = true;
        var recipient = new EmailRecipient { Email = TestEmailRecipient.Trim() };
        if (!EmailAddressValidator.IsValid(recipient.Email))
        {
            _dialogService.ShowWarning("Gecersiz alici", "Test e-postasi icin gecerli bir alici adresi girin.");
            return;
        }

        var context = new NotificationTemplateContext
        {
            DeviceName = "Ornek Kamera",
            IpAddress = "192.0.2.10",
            DeviceType = DeviceType.Camera.ToDisplayName(),
            GroupName = "Ornek Grup",
            Status = DeviceStatus.Offline.ToDisplayName(),
            IncidentStartedAtUtc = DateTime.UtcNow.AddHours(-2),
            LastSuccessfulCheckAtUtc = DateTime.UtcNow.AddHours(-3),
            LastCheckAtUtc = DateTime.UtcNow,
            OfflineDuration = TimeSpan.FromHours(2),
            EscalationThreshold = TimeSpan.FromHours(settings.Notifications.EscalationThresholdHours)
        };

        IsBusy = true;
        IsSendingTestEmail = true;
        SmtpTestResult = "Test e-postasi gonderiliyor...";
        try
        {
            var renderer = new NetworkHealthMonitor.Services.NotificationTemplateRenderer();
            var subject = renderer.Render(settings.Notifications.EmailTemplates.InitialOfflineSubject, context);
            var body = renderer.Render(settings.Notifications.EmailTemplates.InitialOfflineBody, context);
            var result = await _emailSender.SendAsync(
                settings.Notifications,
                recipient,
                subject,
                body,
                settings.Notifications.EmailTemplates.IsHtml);
            SmtpTestResult = result.Success
                ? "Test e-postasi basariyla gonderildi."
                : result.SafeErrorMessage;

            settings.Notifications.LastTestAtUtc = DateTime.UtcNow;
            settings.Notifications.LastTestResult = SmtpTestResult;
            await _settingsService.SaveAsync(settings);
            ApplySettings(settings);
        }
        catch (Exception ex)
        {
            SmtpTestResult = ex.Message;
        }
        finally
        {
            IsSendingTestEmail = false;
            IsBusy = false;
        }
    }

    private void AddInitialEmailRecipient()
    {
        AddEmailRecipient(InitialEmailRecipients, NewInitialEmailAddress, value => NewInitialEmailAddress = value);
    }

    private void RemoveInitialEmailRecipient()
    {
        if (SelectedInitialEmailRecipient is not null)
        {
            InitialEmailRecipients.Remove(SelectedInitialEmailRecipient);
            SelectedInitialEmailRecipient = null;
        }
    }

    private void AddEscalationEmailRecipient()
    {
        AddEmailRecipient(EscalationEmailRecipients, NewEscalationEmailAddress, value => NewEscalationEmailAddress = value);
    }

    private void RemoveEscalationEmailRecipient()
    {
        if (SelectedEscalationEmailRecipient is not null)
        {
            EscalationEmailRecipients.Remove(SelectedEscalationEmailRecipient);
            SelectedEscalationEmailRecipient = null;
        }
    }

    private void AddEmailRecipient(
        System.Collections.ObjectModel.ObservableCollection<EmailRecipient> target,
        string email,
        Action<string> resetInput)
    {
        if (!EmailAddressValidator.IsValid(email))
        {
            _dialogService.ShowWarning("Gecersiz e-posta", "Gecerli bir e-posta adresi girin.");
            return;
        }

        var normalized = email.Trim().ToLowerInvariant();
        if (target.Any(recipient => string.Equals(recipient.NormalizedEmail, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            _dialogService.ShowWarning("Yinelenen e-posta", "Bu adres listede zaten var.");
            return;
        }

        target.Add(new EmailRecipient { Email = email.Trim() });
        resetInput(string.Empty);
    }

    private void ResetEmailTemplates()
    {
        InitialOfflineEmailSubjectTemplate = NotificationTemplateDefaults.InitialOfflineSubject;
        InitialOfflineEmailBodyTemplate = NotificationTemplateDefaults.InitialOfflineBody;
        EscalationEmailSubjectTemplate = NotificationTemplateDefaults.EscalationSubject;
        EscalationEmailBodyTemplate = NotificationTemplateDefaults.EscalationBody;
        RecoveredEmailSubjectTemplate = NotificationTemplateDefaults.RecoveredSubject;
        RecoveredEmailBodyTemplate = NotificationTemplateDefaults.RecoveredBody;
        EmailTemplatesAreHtml = false;
    }

    private static void OpenLogFolder()
    {
        Directory.CreateDirectory(NetworkHealthMonitor.Data.DatabasePaths.LogDirectory);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = NetworkHealthMonitor.Data.DatabasePaths.LogDirectory,
            UseShellExecute = true
        });
    }

    private async Task RunStartupRetentionCleanupAsync(AppSettings settings)
    {
        var deleted = 0;
        if (settings.LogRetentionDays <= 0)
        {
            deleted += await _maintenanceService.ApplyRetentionAsync(settings);
            if (deleted > 0)
            {
                await _maintenanceService.OptimizeDatabaseAsync();
            }

            return;
        }

        deleted += await _pingLogRepository.ClearOlderThanAsync(DateTime.Now.AddDays(-settings.LogRetentionDays));
        deleted += await _maintenanceService.ApplyRetentionAsync(settings);
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
        OpenUiOnWindowsLogin = settings.OpenUiOnWindowsLogin;
        CsvDelimiter = settings.CsvDelimiter;
        LogRetentionDays = settings.LogRetentionDays;
        AvailabilityPeriodRetentionDays = settings.AvailabilityPeriodRetentionDays;
        IncidentRetentionDays = settings.IncidentRetentionDays;
        DailyAggregateRetentionDays = settings.DailyAggregateRetentionDays;
        HeartbeatGraceSeconds = settings.HeartbeatGraceSeconds;
        ExpectedCheckGraceMultiplier = settings.ExpectedCheckGraceMultiplier;
        DowntimeStartPolicy = settings.DowntimeStartPolicy;
        ExportDirectory = settings.ExportDirectory;
        ReplaceCollection(DeviceTypePolicies, DeviceTypePolicy.NormalizeCollection(settings.DeviceTypePolicies));
        NotificationEnabled = settings.Notifications.Enabled;
        NotificationBaseUrl = settings.Notifications.BaseUrl;
        NotificationTopic = settings.Notifications.Topic;
        NotificationAccessToken = settings.Notifications.AccessToken;
        NotificationIncludeIpAddress = settings.Notifications.IncludeIpAddress;
        NotificationNotifyOnDown = settings.Notifications.NotifyOnDeviceDown;
        NotificationNotifyOnRecovered = settings.Notifications.NotifyOnDeviceRecovered;
        NotificationDownFailureThreshold = settings.Notifications.DownFailureThreshold;
        NotificationRecoverySuccessThreshold = settings.Notifications.RecoverySuccessThreshold;
        NotificationCooldownMinutes = settings.Notifications.NotificationCooldownMinutes;
        NotificationRequestTimeoutSeconds = settings.Notifications.RequestTimeoutSeconds;
        NotificationMaxRetryCount = settings.Notifications.MaxRetryCount;
        NotificationInitialRetryDelaySeconds = settings.Notifications.InitialRetryDelaySeconds;
        EmailEnabled = settings.Notifications.EmailEnabled;
        SmtpHost = settings.Notifications.SmtpHost;
        SmtpPort = settings.Notifications.SmtpPort;
        SmtpSecurity = settings.Notifications.SmtpSecurity;
        AllowInsecureSmtp = settings.Notifications.AllowInsecureSmtp;
        SmtpUsername = settings.Notifications.SmtpUsername;
        SmtpPassword = settings.Notifications.SmtpPassword;
        SenderEmail = settings.Notifications.SenderEmail;
        SenderDisplayName = settings.Notifications.SenderDisplayName;
        SmtpConnectionTimeoutSeconds = settings.Notifications.SmtpConnectionTimeoutSeconds;
        EmailMaxRetryCount = settings.Notifications.EmailMaxRetryCount;
        TestEmailRecipient = settings.Notifications.TestEmailRecipient;
        EmailEscalationThresholdHours = settings.Notifications.EscalationThresholdHours;
        NotificationNotifyOnEscalated = settings.Notifications.NotifyOnDeviceEscalated;
        EmailNotifyOnRecovered = settings.Notifications.EmailNotifyOnDeviceRecovered;
        ReplaceCollection(InitialEmailRecipients, settings.Notifications.InitialEmailRecipients.Select(CloneRecipient));
        ReplaceCollection(EscalationEmailRecipients, settings.Notifications.EscalationEmailRecipients.Select(CloneRecipient));
        InitialOfflineEmailSubjectTemplate = settings.Notifications.EmailTemplates.InitialOfflineSubject;
        InitialOfflineEmailBodyTemplate = settings.Notifications.EmailTemplates.InitialOfflineBody;
        EscalationEmailSubjectTemplate = settings.Notifications.EmailTemplates.EscalationSubject;
        EscalationEmailBodyTemplate = settings.Notifications.EmailTemplates.EscalationBody;
        RecoveredEmailSubjectTemplate = settings.Notifications.EmailTemplates.RecoveredSubject;
        RecoveredEmailBodyTemplate = settings.Notifications.EmailTemplates.RecoveredBody;
        EmailTemplatesAreHtml = settings.Notifications.EmailTemplates.IsHtml;
        NotificationLastTestResult = settings.Notifications.LastTestResult;
        NotificationLastSuccessfulAtText = settings.Notifications.LastSuccessfulNotificationAtUtc.HasValue
            ? settings.Notifications.LastSuccessfulNotificationAtUtc.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss")
            : "-";
        NotificationLastError = settings.Notifications.LastNotificationError;
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

