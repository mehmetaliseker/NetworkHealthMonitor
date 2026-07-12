using System.IO;
using NetworkHealthMonitor.Data;

namespace NetworkHealthMonitor.Infrastructure;

public static class AppErrorLogger
{
    private static readonly object SyncRoot = new();

    public static string LogFilePath => Path.Combine(DatabasePaths.LogDirectory, "network_health_monitor.log");

    public static void Log(Exception exception, string context)
    {
        Write($"[{DateTime.Now:O}] {context}{Environment.NewLine}{exception}{Environment.NewLine}");
    }

    public static void LogInfo(string message)
    {
        Write($"[{DateTime.Now:O}] INFO {message}{Environment.NewLine}");
    }

    private static void Write(string entry)
    {
        try
        {
            Directory.CreateDirectory(DatabasePaths.LogDirectory);

            lock (SyncRoot)
            {
                File.AppendAllText(LogFilePath, entry);
            }
        }
        catch
        {
            // Logging must never mask the original application error.
        }
    }
}
