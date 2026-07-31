using NetworkHealthMonitor.ViewModels.Pages;
using NetworkHealthMonitor.ViewModels.Shell;

namespace NetworkHealthMonitor.ViewModels.PingHistory;

public sealed class PingHistoryViewModel : LegacyPageViewModelBase
{
    public PingHistoryViewModel(MainViewModel legacy, ShellViewModel shell)
        : base(legacy, shell, "Ping Geçmişi", "Ping kayıtlarını tarih, cihaz, grup ve durumla filtreleyin.")
    {
    }

    protected override bool ResolveHasData()
    {
        return Legacy.Logs.Count > 0;
    }
}
