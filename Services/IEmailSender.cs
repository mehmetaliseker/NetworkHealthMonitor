using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public interface IEmailSender
{
    Task<NotificationSendResult> SendAsync(
        NotificationSettings settings,
        EmailRecipient recipient,
        string subject,
        string body,
        bool isHtml,
        CancellationToken cancellationToken = default);

    Task<NotificationSendResult> TestConnectionAsync(
        NotificationSettings settings,
        CancellationToken cancellationToken = default);
}
