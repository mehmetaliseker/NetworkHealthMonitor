using NetworkHealthMonitor.ViewModels.Pages;
using NetworkHealthMonitor.ViewModels.Shell;

namespace NetworkHealthMonitor.ViewModels.Reports;

public sealed class ReportsViewModel : LegacyPageViewModelBase
{
    public ReportsViewModel(MainViewModel legacy, ShellViewModel shell)
        : base(legacy, shell, "Raporlar", "Uptime, SLA, kesinti ve bildirim başarı raporlarını oluşturun.")
    {
    }

    protected override bool ResolveHasData()
    {
        return Legacy.ReportOptions.Count > 0;
    }
}
