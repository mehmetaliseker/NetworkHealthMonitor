namespace NetworkHealthMonitor.Tray.Services;

public sealed record TrayMenuState(
    string StatusText,
    string ToolTipText,
    bool ShowInstall,
    bool CanStart,
    bool CanStop,
    bool CanRestart);
