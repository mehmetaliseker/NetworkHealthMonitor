namespace NetworkHealthMonitor.Models;

public sealed record NtfyPublishResult(
    bool Success,
    bool IsTransient,
    int? StatusCode,
    TimeSpan? RetryAfter,
    string SafeErrorMessage,
    string UserMessage = "",
    string TechnicalDetail = "",
    string FailureKind = "")
{
    public static NtfyPublishResult Ok()
    {
        return new NtfyPublishResult(
            true,
            false,
            null,
            null,
            string.Empty,
            "Test bildirimi başarıyla gönderildi.",
            string.Empty,
            string.Empty);
    }
}
