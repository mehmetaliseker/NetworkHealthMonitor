namespace NetworkHealthMonitor.Models;

public sealed class AppSettings
{
    public int PingTimeoutMs { get; set; } = 1000;

    public int MaxParallelPings { get; set; } = 32;

    public int DefaultFailureThreshold { get; set; } = 3;

    public bool AutoCheckEnabled { get; set; }

    public int AutoCheckIntervalMinutes { get; set; } = 5;

    public bool StartSchedulePlansOnStartup { get; set; } = true;

    public string CsvDelimiter { get; set; } = ";";

    public int LogRetentionDays { get; set; } = 30;

    public string Theme { get; set; } = "Açık";

    public static AppSettings Default => new();
}
