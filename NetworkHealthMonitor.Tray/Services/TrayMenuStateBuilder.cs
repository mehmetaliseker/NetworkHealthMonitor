namespace NetworkHealthMonitor.Tray.Services;

public static class TrayMenuStateBuilder
{
    public static TrayMenuState Build(WorkerServiceStatus status)
    {
        var statusText = status.State == WorkerServiceState.NotInstalled
            ? "Durum: Worker kurulu değil"
            : $"Durum: {status.DisplayText}";

        var tooltip = status.State switch
        {
            WorkerServiceState.Running => "Network Health Monitor - Worker çalışıyor",
            WorkerServiceState.Stopped => "Network Health Monitor - Worker durduruldu",
            WorkerServiceState.StartPending => "Network Health Monitor - Worker başlatılıyor",
            WorkerServiceState.StopPending => "Network Health Monitor - Worker durduruluyor",
            WorkerServiceState.NotInstalled => "Network Health Monitor - Worker kurulu değil",
            WorkerServiceState.Error => "Network Health Monitor - Worker hatası",
            _ => "Network Health Monitor - Worker durumu bilinmiyor"
        };

        return new TrayMenuState(
            statusText,
            tooltip,
            ShowInstall: status.State == WorkerServiceState.NotInstalled,
            CanStart: status.State == WorkerServiceState.Stopped,
            CanStop: status.State == WorkerServiceState.Running,
            CanRestart: status.State == WorkerServiceState.Running);
    }
}
