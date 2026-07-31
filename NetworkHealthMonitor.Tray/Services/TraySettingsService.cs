using System.Text.Json;
using System.IO;
using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Infrastructure;

namespace NetworkHealthMonitor.Tray.Services;

public sealed class TraySettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string SettingsPath => Path.Combine(DatabasePaths.ConfigDirectory, "tray-settings.json");

    public async Task<TraySettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        DatabasePaths.EnsureDirectories();
        if (!File.Exists(SettingsPath))
        {
            return new TraySettings();
        }

        try
        {
            await using var stream = File.OpenRead(SettingsPath);
            return await JsonSerializer.DeserializeAsync<TraySettings>(stream, cancellationToken: cancellationToken)
                   ?? new TraySettings();
        }
        catch (Exception ex)
        {
            AppErrorLogger.Log(ex, "Tray settings could not be loaded.");
            return new TraySettings();
        }
    }

    public async Task SaveAsync(TraySettings settings, CancellationToken cancellationToken = default)
    {
        DatabasePaths.EnsureDirectories();
        await using var stream = File.Create(SettingsPath);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
    }
}
