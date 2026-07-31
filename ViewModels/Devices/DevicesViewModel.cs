using NetworkHealthMonitor.ViewModels.Pages;
using NetworkHealthMonitor.ViewModels.Shell;

namespace NetworkHealthMonitor.ViewModels.Devices;

public sealed class DevicesViewModel : LegacyPageViewModelBase
{
    public DevicesViewModel(MainViewModel legacy, ShellViewModel shell)
        : base(legacy, shell, "Cihazlar", "Ağda izlenecek cihazları ekleyin, düzenleyin ve yönetin.")
    {
    }

    protected override bool ResolveHasData()
    {
        return !Legacy.HasNoDevices;
    }
}
