using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace NetworkHealthMonitor.Models;

public static class NotificationEventTypes
{
    public const string DeviceSuspectedOffline = "DeviceSuspectedOffline";
    public const string DeviceOfflineEscalated = "DeviceOfflineEscalated";
    public const string DeviceRecovered = "DeviceRecovered";
    public const string DeviceDownLegacy = "DeviceDown";
    public const string Test = "Test";
}

public static class NotificationChannels
{
    public const string Ntfy = "Ntfy";
    public const string Email = "Email";
}

public static class NotificationStatuses
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Sent = "Sent";
    public const string Failed = "Failed";
    public const string DeadLetter = "DeadLetter";
    public const string Cancelled = "Cancelled";
}

public enum SmtpSecurityMode
{
    StartTls = 0,
    SslTls = 1,
    None = 2
}

public sealed class EmailRecipient
{
    public string Email { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string NormalizedEmail => Email.Trim().ToLowerInvariant();

    public string DisplayText => string.IsNullOrWhiteSpace(DisplayName)
        ? Email.Trim()
        : $"{DisplayName.Trim()} <{Email.Trim()}>";
}

public sealed class EmailTemplateSettings
{
    public string InitialOfflineSubject { get; set; } = NotificationTemplateDefaults.InitialOfflineSubject;

    public string InitialOfflineBody { get; set; } = NotificationTemplateDefaults.InitialOfflineBody;

    public string EscalationSubject { get; set; } = NotificationTemplateDefaults.EscalationSubject;

    public string EscalationBody { get; set; } = NotificationTemplateDefaults.EscalationBody;

    public string RecoveredSubject { get; set; } = NotificationTemplateDefaults.RecoveredSubject;

    public string RecoveredBody { get; set; } = NotificationTemplateDefaults.RecoveredBody;

    public bool IsHtml { get; set; }
}

public static class NotificationTemplateDefaults
{
    public const string ApplicationName = "NetworkHealthMonitor";

    public const string InitialOfflineSubject = "[NetworkHealthMonitor] {DeviceName} muhtemelen erisilemiyor";

    public const string InitialOfflineBody = """
        Merhaba,

        {DeviceName} cihazi akilli tekrar deneme surecinden sonra muhtemelen erisilemiyor durumuna gecti.

        Cihaz: {DeviceName}
        IP: {IpAddress}
        Tip: {DeviceType}
        Grup: {GroupName}
        Durum: {Status}
        Olay baslangici: {IncidentStartedAt}
        Son basarili kontrol: {LastSuccessfulCheckAt}
        Son kontrol: {LastCheckAt}

        Bu bildirim ayni kesinti olayi icin yalnizca bir kez gonderilir.
        """;

    public const string EscalationSubject = "[NetworkHealthMonitor] {DeviceName} uzun suredir erisilemiyor";

    public const string EscalationBody = """
        Merhaba,

        {DeviceName} cihazi {OfflineDuration} suredir erisilemiyor. Escalation esigi: {EscalationThreshold}.

        Cihaz: {DeviceName}
        IP: {IpAddress}
        Tip: {DeviceType}
        Grup: {GroupName}
        Durum: {Status}
        Olay baslangici: {IncidentStartedAt}
        Son basarili kontrol: {LastSuccessfulCheckAt}
        Son kontrol: {LastCheckAt}
        """;

    public const string RecoveredSubject = "[NetworkHealthMonitor] {DeviceName} tekrar erisilebilir";

    public const string RecoveredBody = """
        Merhaba,

        {DeviceName} cihazi tekrar erisilebilir durumda.

        Cihaz: {DeviceName}
        IP: {IpAddress}
        Tip: {DeviceType}
        Grup: {GroupName}
        Kesinti baslangici: {IncidentStartedAt}
        Son kontrol: {LastCheckAt}
        Kesinti suresi: {OfflineDuration}
        """;

    public static EmailTemplateSettings Create() => new();
}

public static class NotificationTemplatePlaceholders
{
    public static readonly IReadOnlyList<string> All = new[]
    {
        "DeviceName",
        "IpAddress",
        "DeviceType",
        "GroupName",
        "Status",
        "IncidentStartedAt",
        "LastSuccessfulCheckAt",
        "LastCheckAt",
        "OfflineDuration",
        "EscalationThreshold",
        "ApplicationName"
    };
}

public sealed class NotificationTemplateContext
{
    public string DeviceName { get; init; } = string.Empty;

    public string IpAddress { get; init; } = string.Empty;

    public string DeviceType { get; init; } = string.Empty;

    public string GroupName { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public DateTime IncidentStartedAtUtc { get; init; }

    public DateTime? LastSuccessfulCheckAtUtc { get; init; }

    public DateTime LastCheckAtUtc { get; init; }

    public TimeSpan OfflineDuration { get; init; }

    public TimeSpan EscalationThreshold { get; init; }

    public string ApplicationName { get; init; } = NotificationTemplateDefaults.ApplicationName;

    public IReadOnlyDictionary<string, string> ToValues()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DeviceName"] = DeviceName,
            ["IpAddress"] = IpAddress,
            ["DeviceType"] = DeviceType,
            ["GroupName"] = string.IsNullOrWhiteSpace(GroupName) ? "-" : GroupName,
            ["Status"] = Status,
            ["IncidentStartedAt"] = IncidentStartedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture),
            ["LastSuccessfulCheckAt"] = LastSuccessfulCheckAtUtc.HasValue
                ? LastSuccessfulCheckAtUtc.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture)
                : "-",
            ["LastCheckAt"] = LastCheckAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture),
            ["OfflineDuration"] = FormatDuration(OfflineDuration),
            ["EscalationThreshold"] = FormatDuration(EscalationThreshold),
            ["ApplicationName"] = ApplicationName
        };
    }

    private static string FormatDuration(TimeSpan value)
    {
        if (value.TotalDays >= 1)
        {
            return $"{(int)value.TotalDays} gun {value.Hours} sa {value.Minutes} dk";
        }

        if (value.TotalHours >= 1)
        {
            return $"{(int)value.TotalHours} sa {value.Minutes} dk";
        }

        return $"{Math.Max(0, (int)value.TotalMinutes)} dk {value.Seconds} sn";
    }
}

public sealed class NotificationOutboxCreateRequest
{
    public string EventType { get; init; } = string.Empty;

    public int? DeviceId { get; init; }

    public long? IncidentId { get; init; }

    public string Channel { get; init; } = NotificationChannels.Ntfy;

    public string Recipient { get; init; } = string.Empty;

    public string Subject { get; init; } = string.Empty;

    public string Body { get; init; } = string.Empty;

    public string PayloadJson { get; init; } = string.Empty;

    public string IdempotencyKey { get; init; } = string.Empty;
}

public sealed record NotificationSendResult(
    bool Success,
    bool IsTransient,
    string SafeErrorMessage,
    TimeSpan? RetryAfter = null)
{
    public static NotificationSendResult Ok() => new(true, false, string.Empty);

    public static NotificationSendResult PermanentFailure(string message) => new(false, false, message);

    public static NotificationSendResult TransientFailure(string message, TimeSpan? retryAfter = null) => new(false, true, message, retryAfter);
}

public static class EmailAddressValidator
{
    private static readonly Regex ControlCharacters = new("[\\r\\n]", RegexOptions.Compiled);

    public static bool IsValid(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || ControlCharacters.IsMatch(email))
        {
            return false;
        }

        try
        {
            var address = new MailAddress(email.Trim());
            return string.Equals(address.Address, email.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
