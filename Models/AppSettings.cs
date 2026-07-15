namespace NetworkHealthMonitor.Models;

public sealed class AppSettings
{
    public const int MinPingTimeoutMs = 250;
    public const int MaxPingTimeoutMs = 10000;
    public const int DefaultPingTimeoutMs = 1000;
    public const int MinParallelPings = 1;
    public const int MaxParallelPingsLimit = 128;
    public const int DefaultMaxParallelPings = 32;
    public const int DefaultSchedulePlanIntervalMinutes = 10;
    public const int MinSchedulePlanIntervalMinutes = 10;
    public const int MaxSchedulePlanIntervalMinutes = 360;
    public const int DefaultSchedulePlanMaxParallelism = 16;
    public const int MinFailureThreshold = 1;
    public const int MaxFailureThreshold = 20;
    public const int DefaultFailureThresholdValue = 3;
    public const int MinAutoCheckIntervalMinutes = 1;
    public const int DefaultAutoCheckIntervalMinutes = 5;
    public const int MinFailureRetryIntervalSeconds = 10;
    public const int MaxFailureRetryIntervalSeconds = 3600;
    public const int DefaultFailureRetryIntervalSecondsValue = 60;
    public const int MinFailureRetryLimit = 1;
    public const int MaxFailureRetryLimit = 20;
    public const int DefaultFailureRetryLimitValue = 3;
    public const int MinDeviceCheckIntervalSeconds = 30;
    public const int MaxDeviceCheckIntervalSeconds = 86400;
    public const int MinSchedulerPollIntervalSeconds = 5;
    public const int MaxSchedulerPollIntervalSeconds = 300;
    public const int DefaultSchedulerPollIntervalSeconds = 15;
    public const int SqliteBusyTimeoutMs = 5000;
    public const int DefaultLogRetentionDays = 90;
    public const int DefaultHeartbeatGraceSeconds = 120;
    public const int DefaultExpectedCheckGraceMultiplier = 2;
    public const int DefaultAvailabilityPeriodRetentionDays = 730;
    public const int DefaultIncidentRetentionDays = 1825;
    public const int DefaultDailyAggregateRetentionDays = 0;
    public const string DefaultCsvDelimiter = ";";

    public int PingTimeoutMs { get; set; } = DefaultPingTimeoutMs;

    public int MaxParallelPings { get; set; } = DefaultMaxParallelPings;

    public int DefaultFailureThreshold { get; set; } = DefaultFailureThresholdValue;

    public int AutoCheckIntervalMinutes { get; set; } = DefaultAutoCheckIntervalMinutes;

    public int SchedulerPollIntervalSeconds { get; set; } = DefaultSchedulerPollIntervalSeconds;

    public bool AutoCheckEnabled { get; set; } = true;

    public int DefaultFailureRetryIntervalSeconds { get; set; } = DefaultFailureRetryIntervalSecondsValue;

    public int DefaultFailureRetryLimit { get; set; } = DefaultFailureRetryLimitValue;

    public bool StartSchedulePlansOnStartup { get; set; } = true;

    public bool OpenUiOnWindowsLogin { get; set; }

    public string CsvDelimiter { get; set; } = DefaultCsvDelimiter;

    public int LogRetentionDays { get; set; } = DefaultLogRetentionDays;

    public int AvailabilityPeriodRetentionDays { get; set; } = DefaultAvailabilityPeriodRetentionDays;

    public int IncidentRetentionDays { get; set; } = DefaultIncidentRetentionDays;

    public int DailyAggregateRetentionDays { get; set; } = DefaultDailyAggregateRetentionDays;

    public int HeartbeatGraceSeconds { get; set; } = DefaultHeartbeatGraceSeconds;

    public int ExpectedCheckGraceMultiplier { get; set; } = DefaultExpectedCheckGraceMultiplier;

    public DowntimeStartPolicy DowntimeStartPolicy { get; set; } = DowntimeStartPolicy.FirstFailedCheck;

    public string ExportDirectory { get; set; } = string.Empty;

    public List<DeviceTypePolicy> DeviceTypePolicies { get; set; } = DeviceTypePolicy.CreateDefaults().ToList();

    public NotificationSettings Notifications { get; set; } = new();

    public string Theme { get; set; } = "Açık";

    public static AppSettings Default => new();
}
