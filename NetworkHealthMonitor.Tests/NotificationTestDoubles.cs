using NetworkHealthMonitor.Models;
using NetworkHealthMonitor.Services;

namespace NetworkHealthMonitor.Tests;

internal sealed class FakeClock : IClock
{
    public FakeClock(DateTime utcNow)
    {
        UtcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
    }

    public DateTime UtcNow { get; private set; }

    public void Advance(TimeSpan value)
    {
        UtcNow = UtcNow.Add(value);
    }
}

internal sealed class FakeNotificationChannel : INotificationChannel
{
    private readonly Func<NotificationOutboxItem, NotificationSendResult> _send;

    public FakeNotificationChannel(
        string name,
        Func<NotificationOutboxItem, NotificationSendResult>? send = null,
        bool enabled = true,
        int maxRetryCount = 3,
        int initialRetryDelaySeconds = 1)
    {
        Name = name;
        IsChannelEnabled = enabled;
        MaxRetryCountValue = maxRetryCount;
        InitialRetryDelaySecondsValue = initialRetryDelaySeconds;
        _send = send ?? (_ => NotificationSendResult.Ok());
    }

    public string Name { get; }

    public bool IsChannelEnabled { get; set; }

    public int MaxRetryCountValue { get; set; }

    public int InitialRetryDelaySecondsValue { get; set; }

    public List<NotificationOutboxItem> SentItems { get; } = new();

    public bool IsEnabled(NotificationSettings settings) => IsChannelEnabled;

    public int MaxRetryCount(NotificationSettings settings) => MaxRetryCountValue;

    public int InitialRetryDelaySeconds(NotificationSettings settings) => InitialRetryDelaySecondsValue;

    public Task<NotificationSendResult> SendAsync(
        NotificationOutboxItem item,
        NotificationSettings settings,
        CancellationToken cancellationToken = default)
    {
        SentItems.Add(item);
        return Task.FromResult(_send(item));
    }
}
