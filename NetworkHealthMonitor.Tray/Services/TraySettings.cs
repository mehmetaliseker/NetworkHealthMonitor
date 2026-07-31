namespace NetworkHealthMonitor.Tray.Services;

public sealed class TraySettings
{
    public bool StartTrayOnWindowsLogin { get; set; }

    public bool StartWorkerWhenTrayStarts { get; set; }
}
