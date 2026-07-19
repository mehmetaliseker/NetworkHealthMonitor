using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class EmailNotificationChannel : INotificationChannel
{
    private readonly IEmailSender _emailSender;

    public EmailNotificationChannel(IEmailSender emailSender)
    {
        _emailSender = emailSender;
    }

    public string Name => NotificationChannels.Email;

    public bool IsEnabled(NotificationSettings settings) => settings.EmailEnabled;

    public int MaxRetryCount(NotificationSettings settings) => settings.EmailMaxRetryCount;

    public int InitialRetryDelaySeconds(NotificationSettings settings) => Math.Max(1, settings.InitialRetryDelaySeconds);

    public Task<NotificationSendResult> SendAsync(
        NotificationOutboxItem item,
        NotificationSettings settings,
        CancellationToken cancellationToken = default)
    {
        return _emailSender.SendAsync(
            settings,
            new EmailRecipient { Email = item.Recipient },
            item.Subject,
            item.Body,
            settings.EmailTemplates.IsHtml,
            cancellationToken);
    }
}
