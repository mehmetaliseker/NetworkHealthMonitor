namespace NetworkHealthMonitor.Data;

public interface IApplicationPathProvider
{
    string DataDirectory { get; }

    string DatabasePath { get; }

    string SettingsPath { get; }

    string LogDirectory { get; }

    string BackupDirectory { get; }
}
