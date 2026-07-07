using System.IO;

namespace NetworkHealthMonitor.Data;

public static class DatabasePaths
{
    public const string AppFolderName = "NetworkHealthMonitor";

    public static string AppDataDirectory
    {
        get
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppFolderName);

            Directory.CreateDirectory(path);
            return path;
        }
    }

    public static string DatabaseFilePath => Path.Combine(AppDataDirectory, "network_health_monitor.db");

    public static string SettingsFilePath => Path.Combine(AppDataDirectory, "settings.json");
}
