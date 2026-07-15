using System.IO;

namespace NetworkHealthMonitor.Data;

public sealed class FixedApplicationPathProvider : IApplicationPathProvider
{
    public FixedApplicationPathProvider(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("Data directory cannot be empty.", nameof(rootDirectory));
        }

        RootDirectory = rootDirectory;
    }

    public string RootDirectory { get; }

    public string DataDirectory => Path.Combine(RootDirectory, "data");

    public string ConfigDirectory => Path.Combine(RootDirectory, "config");

    public string DatabasePath => Path.Combine(DataDirectory, "network_health_monitor.db");

    public string SettingsPath => Path.Combine(ConfigDirectory, "settings.json");

    public string LogDirectory => Path.Combine(RootDirectory, "logs");

    public string BackupDirectory => Path.Combine(RootDirectory, "backups");
}
