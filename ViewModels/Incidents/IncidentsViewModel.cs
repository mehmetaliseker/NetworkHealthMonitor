using NetworkHealthMonitor.ViewModels.Pages;
using NetworkHealthMonitor.ViewModels.Shell;

namespace NetworkHealthMonitor.ViewModels.Incidents;

public sealed class IncidentsViewModel : LegacyPageViewModelBase
{
    public IncidentsViewModel(MainViewModel legacy, ShellViewModel shell)
        : base(legacy, shell, "Kesintiler", "Açık ve çözülmüş kesintileri ayrıntılı izleyin.")
    {
    }

    protected override bool ResolveHasData()
    {
        return Legacy.OpenOutages.Count + Legacy.RecentOutages.Count > 0;
    }
}
