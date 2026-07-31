using NetworkHealthMonitor.ViewModels.Pages;
using NetworkHealthMonitor.ViewModels.Shell;

namespace NetworkHealthMonitor.ViewModels.Dashboard;

public sealed class DashboardViewModel : LegacyPageViewModelBase
{
    public DashboardViewModel(MainViewModel legacy, ShellViewModel shell)
        : base(legacy, shell, "Genel Bakış", "Sistemin genel durumunu ve son önemli olayları gösterir.")
    {
    }

    protected override bool ResolveHasData()
    {
        return Legacy.OverviewSummaryCards.Count > 0 || Legacy.RecentAlerts.Count > 0;
    }
}
