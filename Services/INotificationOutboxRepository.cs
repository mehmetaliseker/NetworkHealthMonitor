using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public interface INotificationOutboxRepository
{
    Task<long> AddPendingAsync(
        string eventType,
        int? deviceId,
        long? incidentId,
        string payloadJson,
        string deduplicationKey,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task<long> AddPendingAsync(
        NotificationOutboxCreateRequest request,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationOutboxItem>> ClaimDueAsync(
        int maxItems,
        string lockOwner,
        DateTime nowUtc,
        TimeSpan processingTimeout,
        CancellationToken cancellationToken = default);

    Task MarkSentAsync(long id, DateTime sentAtUtc, CancellationToken cancellationToken = default);

    Task MarkRetryAsync(long id, int attemptCount, DateTime nextAttemptAtUtc, string safeError, CancellationToken cancellationToken = default);

    Task MarkFailedAsync(long id, int attemptCount, string safeError, CancellationToken cancellationToken = default);

    Task MarkDeadLetterAsync(long id, int attemptCount, string safeError, CancellationToken cancellationToken = default);

    Task<int> CancelPendingForDeviceAsync(int deviceId, DateTime nowUtc, CancellationToken cancellationToken = default);

    Task<(int Pending, int Failed)> GetCountsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationOutboxItem>> GetFilteredAsync(
        string? status,
        string? eventType,
        int? deviceId,
        DateTime? startUtc,
        DateTime? endUtc,
        int limit,
        CancellationToken cancellationToken = default);

    Task<int> RetryFailedAsync(IEnumerable<long> ids, DateTime nowUtc, bool resetAttemptCount = true, CancellationToken cancellationToken = default);

    Task<int> CancelPendingAsync(IEnumerable<long> ids, DateTime nowUtc, CancellationToken cancellationToken = default);
}
