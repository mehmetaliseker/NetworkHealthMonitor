using NetworkHealthMonitor.ViewModels.Pages;
using NetworkHealthMonitor.ViewModels.Shell;

namespace NetworkHealthMonitor.ViewModels.Settings;

public sealed class SettingsViewModel : LegacyPageViewModelBase
{
    public SettingsViewModel(MainViewModel legacy, ShellViewModel shell)
        : base(legacy, shell, "Ayarlar", "Genel, kontrol, bildirim, veri, görünüm ve gelişmiş ayarları yönetin.")
    {
    }

    protected override bool ResolveHasData()
    {
        return true;
    }
}
