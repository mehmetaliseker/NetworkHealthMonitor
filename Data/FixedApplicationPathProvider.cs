using System.IO;

namespace NetworkHealthMonitor.Data;

public sealed class FixedApplicationPathProvider : IApplicationPathProvider
{
    public FixedApplicationPathProvider(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            throw new ArgumentException("Data directory cannot be empty.", nameof(dataDirectory));
        }

        DataDirectory = dataDirectory;
    }

    public string DataDirectory { get; }

    public string DatabasePath => Path.Combine(DataDirectory, "network_health_monitor.db");

    public string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public string LogDirectory => Path.Combine(DataDirectory, "logs");

    public string BackupDirectory => Path.Combine(DataDirectory, "backups");
}
