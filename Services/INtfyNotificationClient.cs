using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public interface INtfyNotificationClient
{
    Task<NtfyPublishResult> PublishAsync(
        NotificationSettings settings,
        NtfyNotificationPayload payload,
        CancellationToken cancellationToken = default);
}
