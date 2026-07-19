using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public interface INotificationChannel
{
    string Name { get; }

    bool IsEnabled(NotificationSettings settings);

    int MaxRetryCount(NotificationSettings settings);

    int InitialRetryDelaySeconds(NotificationSettings settings);

    Task<NotificationSendResult> SendAsync(
        NotificationOutboxItem item,
        NotificationSettings settings,
        CancellationToken cancellationToken = default);
}
