namespace NetworkHealthMonitor.Models;

public sealed class NotificationSettings
{
    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "https://ntfy.sh";

    public string Topic { get; set; } = string.Empty;

    public string AccessToken { get; set; } = string.Empty;

    public bool IncludeIpAddress { get; set; } = true;

    public bool NotifyOnDeviceDown { get; set; } = true;

    public bool NotifyOnDeviceRecovered { get; set; } = true;

    public int DownFailureThreshold { get; set; } = 3;

    public int RecoverySuccessThreshold { get; set; } = 2;

    public int NotificationCooldownMinutes { get; set; } = 15;

    public int RequestTimeoutSeconds { get; set; } = 10;

    public int MaxRetryCount { get; set; } = 5;

    public int InitialRetryDelaySeconds { get; set; } = 30;

    public bool AllowInsecureHttp { get; set; }

    public DateTime? LastSuccessfulNotificationAtUtc { get; set; }

    public string LastNotificationError { get; set; } = string.Empty;

    public DateTime? LastTestAtUtc { get; set; }

    public string LastTestResult { get; set; } = string.Empty;
}
