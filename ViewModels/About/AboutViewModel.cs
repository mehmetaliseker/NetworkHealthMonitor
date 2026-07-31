using NetworkHealthMonitor.ViewModels.Pages;
using NetworkHealthMonitor.ViewModels.Shell;

namespace NetworkHealthMonitor.ViewModels.About;

public sealed class AboutViewModel : LegacyPageViewModelBase
{
    public AboutViewModel(MainViewModel legacy, ShellViewModel shell)
        : base(legacy, shell, "Hakkında", "Sürüm, veri yolu ve servis bilgileri.")
    {
    }
}
