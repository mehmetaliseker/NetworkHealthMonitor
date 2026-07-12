using System.IO;

namespace NetworkHealthMonitor.Data;

public static class DatabasePaths
{
    public const string AppFolderName = "NetworkHealthMonitor";

    private static IApplicationPathProvider _pathProvider = new ProgramDataApplicationPathProvider();
    private static string? _legacyDataDirectoryOverride;

    public static IApplicationPathProvider Current => _pathProvider;

    public static void Configure(IApplicationPathProvider pathProvider, string? legacyDataDirectory = null)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        _legacyDataDirectoryOverride = legacyDataDirectory;
        EnsureDirectories();
    }

    public static string DataDirectory => Current.DataDirectory;

    public static string AppDataDirectory => Current.DataDirectory;

    public static string DatabaseFilePath => Current.DatabasePath;

    public static string SettingsFilePath => Current.SettingsPath;

    public static string LogDirectory => Current.LogDirectory;

    public static string BackupDirectory => Current.BackupDirectory;

    public static string LegacyLocalAppDataDirectory
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_legacyDataDirectoryOverride))
            {
                return _legacyDataDirectoryOverride;
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppFolderName);
        }
    }

    public static string LegacyDatabaseFilePath => Path.Combine(LegacyLocalAppDataDirectory, "network_health_monitor.db");

    public static string LegacySettingsFilePath => Path.Combine(LegacyLocalAppDataDirectory, "settings.json");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(Current.DataDirectory);
        Directory.CreateDirectory(Current.LogDirectory);
        Directory.CreateDirectory(Current.BackupDirectory);
    }
}
