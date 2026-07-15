namespace NetworkHealthMonitor.Data;

public interface IApplicationPathProvider
{
    string RootDirectory { get; }

    string DataDirectory { get; }

    string ConfigDirectory { get; }

    string DatabasePath { get; }

    string SettingsPath { get; }

    string LogDirectory { get; }

    string BackupDirectory { get; }
}
