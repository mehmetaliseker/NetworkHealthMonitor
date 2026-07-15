namespace NetworkHealthMonitor.Services;

public interface IAlertPolicyService
{
    bool IsNotificationAllowed(DateTime? lastNotificationAtUtc, DateTime nowUtc, int cooldownMinutes);

    DateTime CalculateNextRetryUtc(int attemptCount, int initialDelaySeconds, TimeSpan? retryAfter, DateTime nowUtc);
}
