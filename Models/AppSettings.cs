namespace NetworkHealthMonitor.Models;

public sealed class AppSettings
{
    public const int MinPingTimeoutMs = 250;
    public const int MaxPingTimeoutMs = 10000;
    public const int DefaultPingTimeoutMs = 1000;
    public const int MinParallelPings = 1;
    public const int MaxParallelPingsLimit = 128;
    public const int DefaultMaxParallelPings = 32;
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
    public const int SchedulerPollIntervalSeconds = 15;
    public const int DefaultLogRetentionDays = 90;

    public int PingTimeoutMs { get; set; } = DefaultPingTimeoutMs;

    public int MaxParallelPings { get; set; } = DefaultMaxParallelPings;

    public int DefaultFailureThreshold { get; set; } = DefaultFailureThresholdValue;

    public int AutoCheckIntervalMinutes { get; set; } = DefaultAutoCheckIntervalMinutes;

    public int DefaultFailureRetryIntervalSeconds { get; set; } = DefaultFailureRetryIntervalSecondsValue;

    public int DefaultFailureRetryLimit { get; set; } = DefaultFailureRetryLimitValue;

    public bool StartSchedulePlansOnStartup { get; set; } = true;

    public string CsvDelimiter { get; set; } = ";";

    public int LogRetentionDays { get; set; } = DefaultLogRetentionDays;

    public string ExportDirectory { get; set; } = string.Empty;

    public string Theme { get; set; } = "Açık";

    public static AppSettings Default => new();
}
