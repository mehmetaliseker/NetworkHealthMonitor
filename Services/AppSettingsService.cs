using System.IO;
using System.Text.Json;
using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public bool LastLoadUsedDefaultsDueToError { get; private set; }

    public async Task<AppSettings> LoadAsync()
    {
        try
        {
            LastLoadUsedDefaultsDueToError = false;
            if (!File.Exists(DatabasePaths.SettingsFilePath))
            {
                var defaults = AppSettings.Default;
                await SaveAsync(defaults);
                return defaults;
            }

            await using var stream = File.OpenRead(DatabasePaths.SettingsFilePath);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions);
            return Normalize(settings ?? AppSettings.Default);
        }
        catch
        {
            LastLoadUsedDefaultsDueToError = true;
            return AppSettings.Default;
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        Directory.CreateDirectory(DatabasePaths.AppDataDirectory);
        var existingProtectedSmtpPassword = await TryReadExistingProtectedSmtpPasswordAsync();
        var persisted = Normalize(settings);
        var secretProtector = new DpapiSecretProtector();
        persisted.Notifications.AccessToken = secretProtector.Protect(persisted.Notifications.AccessToken);
        persisted.Notifications.SmtpPassword = string.IsNullOrEmpty(persisted.Notifications.SmtpPassword)
            && !string.IsNullOrEmpty(existingProtectedSmtpPassword)
                ? existingProtectedSmtpPassword
                : secretProtector.Protect(persisted.Notifications.SmtpPassword);
        await using var stream = File.Create(DatabasePaths.SettingsFilePath);
        await JsonSerializer.SerializeAsync(stream, persisted, JsonOptions);
    }

    private static async Task<string> TryReadExistingProtectedSmtpPasswordAsync()
    {
        try
        {
            if (!File.Exists(DatabasePaths.SettingsFilePath))
            {
                return string.Empty;
            }

            await using var stream = File.OpenRead(DatabasePaths.SettingsFilePath);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions);
            var value = settings?.Notifications?.SmtpPassword ?? string.Empty;
            return value.StartsWith("dpapi:", StringComparison.Ordinal) ? value : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        return new AppSettings
        {
            PingTimeoutMs = Math.Clamp(settings.PingTimeoutMs, AppSettings.MinPingTimeoutMs, AppSettings.MaxPingTimeoutMs),
            MaxParallelPings = Math.Clamp(settings.MaxParallelPings, AppSettings.MinParallelPings, AppSettings.MaxParallelPingsLimit),
            DefaultFailureThreshold = Math.Clamp(settings.DefaultFailureThreshold, AppSettings.MinFailureThreshold, AppSettings.MaxFailureThreshold),
            AutoCheckIntervalMinutes = settings.AutoCheckIntervalMinutes <= 0
                ? AppSettings.DefaultAutoCheckIntervalMinutes
                : Math.Max(AppSettings.MinAutoCheckIntervalMinutes, settings.AutoCheckIntervalMinutes),
            SchedulerPollIntervalSeconds = settings.SchedulerPollIntervalSeconds <= 0
                ? AppSettings.DefaultSchedulerPollIntervalSeconds
                : Math.Clamp(settings.SchedulerPollIntervalSeconds, AppSettings.MinSchedulerPollIntervalSeconds, AppSettings.MaxSchedulerPollIntervalSeconds),
            AutoCheckEnabled = settings.AutoCheckEnabled,
            DefaultFailureRetryIntervalSeconds = settings.DefaultFailureRetryIntervalSeconds <= 0
                ? AppSettings.DefaultFailureRetryIntervalSecondsValue
                : Math.Clamp(settings.DefaultFailureRetryIntervalSeconds, AppSettings.MinFailureRetryIntervalSeconds, AppSettings.MaxFailureRetryIntervalSeconds),
            DefaultFailureRetryLimit = settings.DefaultFailureRetryLimit <= 0
                ? AppSettings.DefaultFailureRetryLimitValue
                : Math.Clamp(settings.DefaultFailureRetryLimit, AppSettings.MinFailureRetryLimit, AppSettings.MaxFailureRetryLimit),
            StartSchedulePlansOnStartup = settings.StartSchedulePlansOnStartup,
            OpenUiOnWindowsLogin = settings.OpenUiOnWindowsLogin,
            CsvDelimiter = string.IsNullOrWhiteSpace(settings.CsvDelimiter) ? AppSettings.DefaultCsvDelimiter : settings.CsvDelimiter[..1],
            LogRetentionDays = settings.LogRetentionDays < 0 ? AppSettings.DefaultLogRetentionDays : settings.LogRetentionDays,
            AvailabilityPeriodRetentionDays = settings.AvailabilityPeriodRetentionDays < 0 ? AppSettings.DefaultAvailabilityPeriodRetentionDays : settings.AvailabilityPeriodRetentionDays,
            IncidentRetentionDays = settings.IncidentRetentionDays < 0 ? AppSettings.DefaultIncidentRetentionDays : settings.IncidentRetentionDays,
            DailyAggregateRetentionDays = settings.DailyAggregateRetentionDays < 0 ? AppSettings.DefaultDailyAggregateRetentionDays : settings.DailyAggregateRetentionDays,
            HeartbeatGraceSeconds = Math.Clamp(settings.HeartbeatGraceSeconds <= 0 ? AppSettings.DefaultHeartbeatGraceSeconds : settings.HeartbeatGraceSeconds, 30, 3600),
            ExpectedCheckGraceMultiplier = Math.Clamp(settings.ExpectedCheckGraceMultiplier <= 0 ? AppSettings.DefaultExpectedCheckGraceMultiplier : settings.ExpectedCheckGraceMultiplier, 1, 10),
            DowntimeStartPolicy = Enum.IsDefined(settings.DowntimeStartPolicy) ? settings.DowntimeStartPolicy : DowntimeStartPolicy.FirstFailedCheck,
            ExportDirectory = settings.ExportDirectory?.Trim() ?? string.Empty,
            DeviceTypePolicies = DeviceTypePolicy.NormalizeCollection(settings.DeviceTypePolicies),
            Notifications = NormalizeNotifications(settings.Notifications),
            Theme = string.IsNullOrWhiteSpace(settings.Theme) ? "Açık" : settings.Theme
        };
    }

    private static NotificationSettings NormalizeNotifications(NotificationSettings? settings)
    {
        settings ??= new NotificationSettings();
        var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl)
            ? "https://ntfy.sh"
            : settings.BaseUrl.Trim().TrimEnd('/');
        var secretProtector = new DpapiSecretProtector();

        return new NotificationSettings
        {
            Enabled = settings.Enabled,
            BaseUrl = baseUrl,
            Topic = settings.Topic?.Trim() ?? string.Empty,
            AccessToken = SafeUnprotect(secretProtector, settings.AccessToken),
            IncludeIpAddress = settings.IncludeIpAddress,
            NotifyOnDeviceDown = settings.NotifyOnDeviceDown,
            NotifyOnDeviceRecovered = settings.NotifyOnDeviceRecovered,
            DownFailureThreshold = Math.Clamp(settings.DownFailureThreshold <= 0 ? 3 : settings.DownFailureThreshold, 1, 20),
            RecoverySuccessThreshold = Math.Clamp(settings.RecoverySuccessThreshold <= 0 ? 2 : settings.RecoverySuccessThreshold, 1, 20),
            NotificationCooldownMinutes = Math.Clamp(settings.NotificationCooldownMinutes < 0 ? 15 : settings.NotificationCooldownMinutes, 0, 1440),
            RequestTimeoutSeconds = Math.Clamp(settings.RequestTimeoutSeconds <= 0 ? 10 : settings.RequestTimeoutSeconds, 1, 120),
            MaxRetryCount = Math.Clamp(settings.MaxRetryCount < 0 ? 5 : settings.MaxRetryCount, 0, 20),
            InitialRetryDelaySeconds = Math.Clamp(settings.InitialRetryDelaySeconds <= 0 ? 30 : settings.InitialRetryDelaySeconds, 1, 3600),
            AllowInsecureHttp = settings.AllowInsecureHttp,
            LastSuccessfulNotificationAtUtc = settings.LastSuccessfulNotificationAtUtc,
            LastNotificationError = settings.LastNotificationError ?? string.Empty,
            LastTestAtUtc = settings.LastTestAtUtc,
            LastTestResult = settings.LastTestResult ?? string.Empty,
            EmailEnabled = settings.EmailEnabled,
            SmtpHost = settings.SmtpHost?.Trim() ?? string.Empty,
            SmtpPort = Math.Clamp(settings.SmtpPort <= 0 ? 587 : settings.SmtpPort, 1, 65535),
            SmtpSecurity = Enum.IsDefined(settings.SmtpSecurity) ? settings.SmtpSecurity : SmtpSecurityMode.StartTls,
            AllowInsecureSmtp = settings.AllowInsecureSmtp,
            SmtpUsername = settings.SmtpUsername?.Trim() ?? string.Empty,
            SmtpPassword = SafeUnprotect(secretProtector, settings.SmtpPassword),
            SenderEmail = settings.SenderEmail?.Trim() ?? string.Empty,
            SenderDisplayName = string.IsNullOrWhiteSpace(settings.SenderDisplayName)
                ? "NetworkHealthMonitor"
                : settings.SenderDisplayName.Trim(),
            SmtpConnectionTimeoutSeconds = Math.Clamp(settings.SmtpConnectionTimeoutSeconds <= 0 ? 30 : settings.SmtpConnectionTimeoutSeconds, 1, 300),
            EmailMaxRetryCount = Math.Clamp(settings.EmailMaxRetryCount < 0 ? 5 : settings.EmailMaxRetryCount, 0, 20),
            TestEmailRecipient = settings.TestEmailRecipient?.Trim() ?? string.Empty,
            EscalationThresholdHours = Math.Clamp(
                settings.EscalationThresholdHours <= 0 ? NotificationSettings.DefaultEscalationThresholdHours : settings.EscalationThresholdHours,
                NotificationSettings.MinEscalationThresholdHours,
                NotificationSettings.MaxEscalationThresholdHours),
            NotifyOnDeviceEscalated = settings.NotifyOnDeviceEscalated,
            EmailNotifyOnDeviceRecovered = settings.EmailNotifyOnDeviceRecovered,
            InitialEmailRecipients = NormalizeRecipients(settings.InitialEmailRecipients),
            EscalationEmailRecipients = NormalizeRecipients(settings.EscalationEmailRecipients),
            EmailTemplates = NormalizeTemplates(settings.EmailTemplates)
        };
    }

    private static string SafeUnprotect(ISecretProtector secretProtector, string? protectedText)
    {
        try
        {
            return secretProtector.Unprotect(protectedText ?? string.Empty);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static List<EmailRecipient> NormalizeRecipients(IEnumerable<EmailRecipient>? recipients)
    {
        if (recipients is null)
        {
            return new List<EmailRecipient>();
        }

        return recipients
            .Select(recipient => new EmailRecipient
            {
                Email = recipient.Email?.Trim() ?? string.Empty,
                DisplayName = recipient.DisplayName?.Trim() ?? string.Empty
            })
            .Where(recipient => EmailAddressValidator.IsValid(recipient.Email))
            .DistinctBy(recipient => recipient.NormalizedEmail)
            .ToList();
    }

    private static EmailTemplateSettings NormalizeTemplates(EmailTemplateSettings? templates)
    {
        templates ??= NotificationTemplateDefaults.Create();
        return new EmailTemplateSettings
        {
            InitialOfflineSubject = string.IsNullOrWhiteSpace(templates.InitialOfflineSubject)
                ? NotificationTemplateDefaults.InitialOfflineSubject
                : templates.InitialOfflineSubject,
            InitialOfflineBody = string.IsNullOrWhiteSpace(templates.InitialOfflineBody)
                ? NotificationTemplateDefaults.InitialOfflineBody
                : templates.InitialOfflineBody,
            EscalationSubject = string.IsNullOrWhiteSpace(templates.EscalationSubject)
                ? NotificationTemplateDefaults.EscalationSubject
                : templates.EscalationSubject,
            EscalationBody = string.IsNullOrWhiteSpace(templates.EscalationBody)
                ? NotificationTemplateDefaults.EscalationBody
                : templates.EscalationBody,
            RecoveredSubject = string.IsNullOrWhiteSpace(templates.RecoveredSubject)
                ? NotificationTemplateDefaults.RecoveredSubject
                : templates.RecoveredSubject,
            RecoveredBody = string.IsNullOrWhiteSpace(templates.RecoveredBody)
                ? NotificationTemplateDefaults.RecoveredBody
                : templates.RecoveredBody,
            IsHtml = templates.IsHtml
        };
    }
}
