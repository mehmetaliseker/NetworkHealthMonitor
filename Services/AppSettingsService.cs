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

    public async Task<AppSettings> LoadAsync()
    {
        try
        {
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
            PingTimeoutMs = Math.Clamp(settings.PingTimeoutMs, 250, 10000),
            MaxParallelPings = Math.Clamp(settings.MaxParallelPings, 1, 128),
            DefaultFailureThreshold = Math.Clamp(settings.DefaultFailureThreshold, 1, 20),
            AutoCheckEnabled = settings.AutoCheckEnabled,
            AutoCheckIntervalMinutes = Math.Max(1, settings.AutoCheckIntervalMinutes),
            StartSchedulePlansOnStartup = settings.StartSchedulePlansOnStartup,
            CsvDelimiter = string.IsNullOrWhiteSpace(settings.CsvDelimiter) ? ";" : settings.CsvDelimiter[..1],
            LogRetentionDays = Math.Max(0, settings.LogRetentionDays),
            Theme = string.IsNullOrWhiteSpace(settings.Theme) ? "Açık" : settings.Theme
        };
    }
}
