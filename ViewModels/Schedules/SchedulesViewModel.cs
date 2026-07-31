using NetworkHealthMonitor.ViewModels.Pages;
using NetworkHealthMonitor.ViewModels.Shell;

namespace NetworkHealthMonitor.ViewModels.Schedules;

public sealed class SchedulesViewModel : LegacyPageViewModelBase
{
    public SchedulesViewModel(MainViewModel legacy, ShellViewModel shell)
        : base(legacy, shell, "Kontrol Planları", "Otomatik ping ve tekrar kontrol kurallarını yönetin.")
    {
    }

    protected override bool ResolveHasData()
    {
        return Legacy.SchedulePlans.Count > 0;
    }
}
