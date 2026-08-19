using System.IO;
using NetworkHealthMonitor.Data;

namespace NetworkHealthMonitor.Tray.Services;

public sealed class TrayPathResolver
{
    public string DataDirectory => DatabasePaths.DataDirectory;

    public string LogDirectory => DatabasePaths.LogDirectory;

    public string ScriptsDirectory
    {
        get
        {
            var installed = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Scripts"));
            if (Directory.Exists(installed))
            {
                return installed;
            }

            var repo = FindRepositoryRoot();
            return repo is null
                ? installed
                : Path.Combine(repo, "scripts");
        }
    }

    public string ManagementUiPath
    {
        get
        {
            var installed = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "UI", "NetworkHealthMonitor.exe"));
            if (File.Exists(installed))
            {
                return installed;
            }

            var repo = FindRepositoryRoot();
            if (repo is not null)
            {
                var configuration = IsReleaseBuild() ? "Release" : "Debug";
                var buildOutput = Path.Combine(repo, "bin", configuration, "net10.0-windows", "NetworkHealthMonitor.exe");
                if (File.Exists(buildOutput))
                {
                    return buildOutput;
                }

                var fallbackOutput = Path.Combine(repo, "bin", "Release", "net10.0-windows", "NetworkHealthMonitor.exe");
                if (File.Exists(fallbackOutput))
                {
                    return fallbackOutput;
                }
            }

            return installed;
        }
    }

    public string WorkerPath
    {
        get
        {
            var installed = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Worker", "NetworkHealthMonitor.Worker.exe"));
            if (File.Exists(installed))
            {
                return installed;
            }

            var repo = FindRepositoryRoot();
            if (repo is not null)
            {
                var configuration = IsReleaseBuild() ? "Release" : "Debug";
                var buildOutput = Path.Combine(repo, "NetworkHealthMonitor.Worker", "bin", configuration, "net10.0-windows", "NetworkHealthMonitor.Worker.exe");
                if (File.Exists(buildOutput))
                {
                    return buildOutput;
                }

                var fallbackOutput = Path.Combine(repo, "NetworkHealthMonitor.Worker", "bin", "Release", "net10.0-windows", "NetworkHealthMonitor.Worker.exe");
                if (File.Exists(fallbackOutput))
                {
                    return fallbackOutput;
                }
            }

            return installed;
        }
    }

    public string ResolveScriptPath(string scriptName)
    {
        return Path.Combine(ScriptsDirectory, scriptName);
    }

    private static bool IsReleaseBuild()
    {
        return AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NetworkHealthMonitor.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
