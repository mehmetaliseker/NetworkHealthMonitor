using System.IO;

namespace NetworkHealthMonitor.Data;

public sealed class ProgramDataApplicationPathProvider : IApplicationPathProvider
{
    public string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        DatabasePaths.AppFolderName);

    public string DataDirectory => Path.Combine(RootDirectory, "data");

    public string ConfigDirectory => Path.Combine(RootDirectory, "config");

    public string DatabasePath => Path.Combine(DataDirectory, "network_health_monitor.db");

    public string SettingsPath => Path.Combine(ConfigDirectory, "settings.json");

    public string LogDirectory => Path.Combine(RootDirectory, "logs");

    public string BackupDirectory => Path.Combine(RootDirectory, "backups");
}
