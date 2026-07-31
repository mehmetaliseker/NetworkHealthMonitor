using NetworkHealthMonitor.ViewModels.Pages;
using NetworkHealthMonitor.ViewModels.Shell;

namespace NetworkHealthMonitor.ViewModels.Notifications;

public sealed class NotificationsViewModel : LegacyPageViewModelBase
{
    public NotificationsViewModel(MainViewModel legacy, ShellViewModel shell)
        : base(legacy, shell, "Bildirimler", "Bildirim geçmişi ve bildirim kurallarını ayrı sekmelerde yönetin.")
    {
    }

    protected override bool ResolveHasData()
    {
        return Legacy.NotificationOutboxItems.Count > 0 || Legacy.NotificationEnabled || Legacy.EmailEnabled;
    }
}
