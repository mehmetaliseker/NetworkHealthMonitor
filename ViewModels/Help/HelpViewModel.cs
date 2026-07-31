using NetworkHealthMonitor.ViewModels.Pages;
using NetworkHealthMonitor.ViewModels.Shell;

namespace NetworkHealthMonitor.ViewModels.Help;

public sealed class HelpViewModel : LegacyPageViewModelBase
{
    public HelpViewModel(MainViewModel legacy, ShellViewModel shell)
        : base(legacy, shell, "Yardım", "Temel kullanım akışlarına hızlı erişim.")
    {
    }
}
