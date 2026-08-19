using NetworkHealthMonitor.ViewModels.Pages;
using NetworkHealthMonitor.ViewModels.Shell;

namespace NetworkHealthMonitor.ViewModels.LiveStatus;

public sealed class LiveStatusViewModel : LegacyPageViewModelBase
{
    public LiveStatusViewModel(MainViewModel legacy, ShellViewModel shell)
        : base(legacy, shell, "Canlı Durum", "Cihazların anlık erişilebilirlik durumunu izleyin.")
    {
    }

    protected override bool ResolveHasData()
    {
        return Legacy.LiveOnlineDevices.Count + Legacy.LiveOfflineDevices.Count + Legacy.LiveCheckedDevices.Count > 0;
    }
}
