namespace NetworkHealthMonitor.Models;

public sealed record PingCheckResult(
    bool IsSuccess,
    long? LatencyMs,
    DateTime CheckedAt,
    string ResponseMessage,
    string ErrorMessage);
