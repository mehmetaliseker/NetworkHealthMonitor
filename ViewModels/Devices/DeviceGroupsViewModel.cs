using NetworkHealthMonitor.ViewModels.Pages;
using NetworkHealthMonitor.ViewModels.Shell;

namespace NetworkHealthMonitor.ViewModels.Devices;

public sealed class DeviceGroupsViewModel : LegacyPageViewModelBase
{
    public DeviceGroupsViewModel(MainViewModel legacy, ShellViewModel shell)
        : base(legacy, shell, "Cihaz Grupları", "Cihazları konum veya işlev bazında izleyin.")
    {
    }

    protected override bool ResolveHasData()
    {
        return Legacy.DeviceGroupSummaries.Count > 0;
    }
}
