using NetworkHealthMonitor.ViewModels.Pages;
using NetworkHealthMonitor.ViewModels.Shell;

namespace NetworkHealthMonitor.ViewModels.Worker;

public sealed class WorkerServiceViewModel : LegacyPageViewModelBase
{
    public WorkerServiceViewModel(MainViewModel legacy, ShellViewModel shell)
        : base(legacy, shell, "Worker Servisi", "Arka plan kontrol servisinin gerçek Windows Service durumunu yönetin.")
    {
    }

    protected override bool ResolveHasData()
    {
        return !string.IsNullOrWhiteSpace(Legacy.WorkerHealthText) || !string.IsNullOrWhiteSpace(Legacy.WorkerStartupTypeText);
    }
}
