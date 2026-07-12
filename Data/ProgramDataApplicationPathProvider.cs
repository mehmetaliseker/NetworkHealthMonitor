using System.IO;

namespace NetworkHealthMonitor.Data;

public sealed class ProgramDataApplicationPathProvider : IApplicationPathProvider
{
    public string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        DatabasePaths.AppFolderName);

    public string DatabasePath => Path.Combine(DataDirectory, "network_health_monitor.db");

    public string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public string LogDirectory => Path.Combine(DataDirectory, "logs");

    public string BackupDirectory => Path.Combine(DataDirectory, "backups");
}
