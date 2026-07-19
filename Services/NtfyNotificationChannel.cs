using System.Text.Json;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class NtfyNotificationChannel : INotificationChannel
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly INtfyNotificationClient _client;

    public NtfyNotificationChannel(INtfyNotificationClient client)
    {
        _client = client;
    }

    public string Name => NotificationChannels.Ntfy;

    public bool IsEnabled(NotificationSettings settings) => settings.Enabled;

    public int MaxRetryCount(NotificationSettings settings) => settings.MaxRetryCount;

    public int InitialRetryDelaySeconds(NotificationSettings settings) => settings.InitialRetryDelaySeconds;

    public async Task<NotificationSendResult> SendAsync(
        NotificationOutboxItem item,
        NotificationSettings settings,
        CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Deserialize<NtfyNotificationPayload>(item.PayloadJson, JsonOptions);
        if (payload is null)
        {
            return NotificationSendResult.PermanentFailure("Invalid ntfy payload.");
        }

        var result = await _client.PublishAsync(settings, payload, cancellationToken);
        if (result.Success)
        {
            return NotificationSendResult.Ok();
        }

        return result.IsTransient
            ? NotificationSendResult.TransientFailure(result.SafeErrorMessage, result.RetryAfter)
            : NotificationSendResult.PermanentFailure(result.SafeErrorMessage);
    }
}
