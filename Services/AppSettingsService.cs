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
        var normalized = Normalize(settings);
        await using var stream = File.Create(DatabasePaths.SettingsFilePath);
        await JsonSerializer.SerializeAsync(stream, normalized, JsonOptions);
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
            CsvDelimiter = string.IsNullOrWhiteSpace(settings.CsvDelimiter) ? AppSettings.DefaultCsvDelimiter : settings.CsvDelimiter[..1],
            LogRetentionDays = settings.LogRetentionDays < 0 ? AppSettings.DefaultLogRetentionDays : settings.LogRetentionDays,
            ExportDirectory = settings.ExportDirectory?.Trim() ?? string.Empty,
            DeviceTypePolicies = DeviceTypePolicy.NormalizeCollection(settings.DeviceTypePolicies),
            Theme = string.IsNullOrWhiteSpace(settings.Theme) ? "Açık" : settings.Theme
        };
    }
}
