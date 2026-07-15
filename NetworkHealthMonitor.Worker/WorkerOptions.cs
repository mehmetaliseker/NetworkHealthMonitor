using NetworkHealthMonitor.Data;

namespace NetworkHealthMonitor.Worker;

public sealed class WorkerOptions
{
    public bool RunOnce { get; init; }

    public bool HealthCheck { get; init; }

    public bool SkipHeartbeatCheck { get; init; }

    public int HeartbeatMaxAgeSeconds { get; init; } = 120;

    public IApplicationPathProvider PathProvider { get; init; } = new ProgramDataApplicationPathProvider();

    public string? LegacyDataDirectory { get; init; }

    public TimeSpan? PollIntervalOverride { get; init; }

    public static WorkerOptions Parse(string[] args)
    {
        var runOnce = false;
        var healthCheck = false;
        var skipHeartbeatCheck = false;
        var heartbeatMaxAgeSeconds = 120;
        string? dataDirectory = null;
        string? legacyDirectory = null;
        TimeSpan? pollInterval = null;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (string.Equals(arg, "--run-once", StringComparison.OrdinalIgnoreCase))
            {
                runOnce = true;
                continue;
            }

            if (string.Equals(arg, "--health-check", StringComparison.OrdinalIgnoreCase))
            {
                healthCheck = true;
                continue;
            }

            if (string.Equals(arg, "--skip-heartbeat-check", StringComparison.OrdinalIgnoreCase))
            {
                skipHeartbeatCheck = true;
                continue;
            }

            if (string.Equals(arg, "--heartbeat-max-age-seconds", StringComparison.OrdinalIgnoreCase)
                && index + 1 < args.Length
                && int.TryParse(args[++index], out var heartbeatSeconds)
                && heartbeatSeconds > 0)
            {
                heartbeatMaxAgeSeconds = heartbeatSeconds;
                continue;
            }

            if (string.Equals(arg, "--data-dir", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                dataDirectory = args[++index];
                continue;
            }

            if (string.Equals(arg, "--legacy-dir", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                legacyDirectory = args[++index];
                continue;
            }

            if (string.Equals(arg, "--poll-seconds", StringComparison.OrdinalIgnoreCase)
                && index + 1 < args.Length
                && int.TryParse(args[++index], out var seconds)
                && seconds > 0)
            {
                pollInterval = TimeSpan.FromSeconds(seconds);
            }
        }

        return new WorkerOptions
        {
            RunOnce = runOnce,
            HealthCheck = healthCheck,
            SkipHeartbeatCheck = skipHeartbeatCheck,
            HeartbeatMaxAgeSeconds = heartbeatMaxAgeSeconds,
            PathProvider = string.IsNullOrWhiteSpace(dataDirectory)
                ? new ProgramDataApplicationPathProvider()
                : new FixedApplicationPathProvider(dataDirectory),
            LegacyDataDirectory = legacyDirectory,
            PollIntervalOverride = pollInterval
        };
    }
}
