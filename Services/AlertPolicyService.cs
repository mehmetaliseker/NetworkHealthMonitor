namespace NetworkHealthMonitor.Services;

public sealed class AlertPolicyService : IAlertPolicyService
{
    public bool IsNotificationAllowed(DateTime? lastNotificationAtUtc, DateTime nowUtc, int cooldownMinutes)
    {
        if (!lastNotificationAtUtc.HasValue || cooldownMinutes <= 0)
        {
            return true;
        }

        return nowUtc - lastNotificationAtUtc.Value.ToUniversalTime() >= TimeSpan.FromMinutes(cooldownMinutes);
    }

    public DateTime CalculateNextRetryUtc(int attemptCount, int initialDelaySeconds, TimeSpan? retryAfter, DateTime nowUtc)
    {
        if (retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero)
        {
            return nowUtc.Add(retryAfter.Value);
        }

        var baseDelay = Math.Max(1, initialDelaySeconds);
        var exponent = Math.Min(Math.Max(0, attemptCount - 1), 8);
        var jitterSeconds = Random.Shared.Next(0, Math.Min(30, baseDelay));
        var delaySeconds = Math.Min(3600, (baseDelay * Math.Pow(2, exponent)) + jitterSeconds);
        return nowUtc.AddSeconds(delaySeconds);
    }
}
