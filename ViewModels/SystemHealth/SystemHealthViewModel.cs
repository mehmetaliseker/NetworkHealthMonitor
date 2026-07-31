using NetworkHealthMonitor.ViewModels.Pages;
using NetworkHealthMonitor.ViewModels.Shell;

namespace NetworkHealthMonitor.ViewModels.SystemHealth;

public sealed class SystemHealthViewModel : LegacyPageViewModelBase
{
    public SystemHealthViewModel(MainViewModel legacy, ShellViewModel shell)
        : base(legacy, shell, "Sistem Sağlığı", "UI, Worker, SQLite, scheduler ve log durumunu izleyin.")
    {
    }

    protected override bool ResolveHasData()
    {
        return !string.IsNullOrWhiteSpace(Legacy.SQLiteStatusText) || Legacy.ServiceReadinessChecks.Count > 0;
    }
}
