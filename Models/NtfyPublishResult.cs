namespace NetworkHealthMonitor.Models;

public sealed record NtfyPublishResult(
    bool Success,
    bool IsTransient,
    int? StatusCode,
    TimeSpan? RetryAfter,
    string SafeErrorMessage)
{
    public static NtfyPublishResult Ok()
    {
        return new NtfyPublishResult(true, false, null, null, string.Empty);
    }
}
