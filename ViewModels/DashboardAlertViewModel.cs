namespace NetworkHealthMonitor.ViewModels;

public sealed class DashboardAlertViewModel
{
    public string Event { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public string TimeText { get; init; } = "-";

    public string Severity { get; init; } = "Bilgi";
}
