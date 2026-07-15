namespace NetworkHealthMonitor.Services;

public interface IUiAutostartService
{
    string ShortcutPath { get; }

    bool IsEnabled(string targetPath);

    Task SetEnabledAsync(bool enabled, string targetPath);
}
