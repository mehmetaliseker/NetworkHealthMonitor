namespace NetworkHealthMonitor.Models;

public sealed record PingExecutionResult(
    IReadOnlyList<PingDeviceResult> Results,
    IReadOnlyList<PingLog> Logs,
    int SkippedBecauseAlreadyRunning)
{
    public int Total => Results.Count + SkippedBecauseAlreadyRunning;

    public int SuccessCount => Results.Count(result => result.IsSuccess);

    public int FailureCount => Results.Count(result => !result.IsSuccess);
}
