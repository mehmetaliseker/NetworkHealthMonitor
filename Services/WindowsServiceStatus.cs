namespace NetworkHealthMonitor.Services;

public sealed record WindowsServiceStatus(string Code, string DisplayText)
{
    public bool IsInstalled { get; init; } = true;

    public bool IsRunning { get; init; }

    public string StartupType { get; init; } = string.Empty;

    public bool IsAutomaticStartup { get; init; }

    public bool RecoveryActionsConfigured { get; init; }

    public string RawStatus { get; init; } = string.Empty;

    public static WindowsServiceStatus Unknown(string displayText)
    {
        return new WindowsServiceStatus("Unknown", displayText) { IsInstalled = false };
    }
}
