using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using NetworkHealthMonitor.Data;

namespace NetworkHealthMonitor.Infrastructure;

public static class ApplicationBuildInfo
{
    public static BuildInfo Current => FromAssembly(Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly());

    public static BuildInfo FromAssembly(Assembly assembly)
    {
        var location = assembly.Location;
        var fileVersionInfo = string.IsNullOrWhiteSpace(location)
            ? null
            : FileVersionInfo.GetVersionInfo(location);
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        return new BuildInfo(
            fileVersionInfo?.ProductVersion ?? informationalVersion ?? assembly.GetName().Version?.ToString() ?? "unknown",
            fileVersionInfo?.FileVersion ?? assembly.GetName().Version?.ToString() ?? "unknown",
            string.IsNullOrWhiteSpace(location) || !File.Exists(location)
                ? "unknown"
                : File.GetLastWriteTimeUtc(location).ToString("O", CultureInfo.InvariantCulture),
            ExtractCommitSha(informationalVersion),
            SqliteConnectionFactory.ExtendedSchedulerSchemaMigrationId);
    }

    private static string ExtractCommitSha(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return "unknown";
        }

        var plusIndex = informationalVersion.LastIndexOf('+');
        if (plusIndex < 0 || plusIndex == informationalVersion.Length - 1)
        {
            return "unknown";
        }

        return informationalVersion[(plusIndex + 1)..];
    }
}

public sealed record BuildInfo(
    string ProductVersion,
    string FileVersion,
    string BuildTimestampUtc,
    string GitCommitSha,
    string ExpectedSchemaVersion);
