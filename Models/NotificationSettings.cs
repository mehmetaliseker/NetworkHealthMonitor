namespace NetworkHealthMonitor.Models;

public sealed class NotificationSettings
{
    public const int MinEscalationThresholdHours = 1;
    public const int MaxEscalationThresholdHours = 365 * 24;
    public const int DefaultEscalationThresholdHours = 48;

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

    public bool EmailEnabled { get; set; }

    public string SmtpHost { get; set; } = string.Empty;

    public int SmtpPort { get; set; } = 587;

    public SmtpSecurityMode SmtpSecurity { get; set; } = SmtpSecurityMode.StartTls;

    public bool AllowInsecureSmtp { get; set; }

    public string SmtpUsername { get; set; } = string.Empty;

    public string SmtpPassword { get; set; } = string.Empty;

    public string SenderEmail { get; set; } = string.Empty;

    public string SenderDisplayName { get; set; } = "NetworkHealthMonitor";

    public int SmtpConnectionTimeoutSeconds { get; set; } = 30;

    public int EmailMaxRetryCount { get; set; } = 5;

    public string TestEmailRecipient { get; set; } = string.Empty;

    public int EscalationThresholdHours { get; set; } = DefaultEscalationThresholdHours;

    public bool NotifyOnDeviceEscalated { get; set; } = true;

    public bool EmailNotifyOnDeviceRecovered { get; set; }

    public List<EmailRecipient> InitialEmailRecipients { get; set; } = new();

    public List<EmailRecipient> EscalationEmailRecipients { get; set; } = new();

    public EmailTemplateSettings EmailTemplates { get; set; } = NotificationTemplateDefaults.Create();
}
