using System.Diagnostics;

namespace NetworkHealthMonitor.Services;

public sealed class WindowsServiceStatusService : IWindowsServiceStatusService
{
    public const string ServiceName = "NetworkHealthMonitorWorker";

    public async Task<WindowsServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var query = await RunScAsync($"query \"{ServiceName}\"", cancellationToken);
            if (query.ExitCode != 0)
            {
                return query.Output.Contains("1060", StringComparison.OrdinalIgnoreCase)
                       || query.Error.Contains("1060", StringComparison.OrdinalIgnoreCase)
                    ? new WindowsServiceStatus("NotFound", "Bulunamadı") { IsInstalled = false }
                    : new WindowsServiceStatus("Inaccessible", "Erişilemiyor") { IsInstalled = false };
            }

            var qc = await RunScAsync($"qc \"{ServiceName}\"", cancellationToken);
            var failure = await RunScAsync($"qfailure \"{ServiceName}\"", cancellationToken);
            return ParseStatus(query.Output, qc.Output, failure.Output);
        }
        catch
        {
            return new WindowsServiceStatus("Inaccessible", "Erişilemiyor") { IsInstalled = false };
        }
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunScAsync(string arguments, CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, output, error);
    }

    public static WindowsServiceStatus ParseStatus(string queryOutput, string qcOutput, string failureOutput)
    {
        var startupType = ParseStartupType(qcOutput);
        var isAutomatic = startupType.Contains("AUTO", StringComparison.OrdinalIgnoreCase);
        var recoveryConfigured = failureOutput.Contains("RESTART", StringComparison.OrdinalIgnoreCase);
        var baseStatus = queryOutput.Contains("RUNNING", StringComparison.OrdinalIgnoreCase)
            ? new WindowsServiceStatus("Running", "Çalışıyor") { IsRunning = true }
            : queryOutput.Contains("START_PENDING", StringComparison.OrdinalIgnoreCase)
                ? new WindowsServiceStatus("StartPending", "Başlatılıyor")
                : queryOutput.Contains("STOP_PENDING", StringComparison.OrdinalIgnoreCase)
                    ? new WindowsServiceStatus("StopPending", "Durduruluyor")
                    : queryOutput.Contains("STOPPED", StringComparison.OrdinalIgnoreCase)
                        ? new WindowsServiceStatus("Stopped", "Durduruldu")
                        : WindowsServiceStatus.Unknown("Erişilemiyor") with { IsInstalled = true };

        return baseStatus with
        {
            StartupType = startupType,
            IsAutomaticStartup = isAutomatic,
            RecoveryActionsConfigured = recoveryConfigured,
            RawStatus = queryOutput + qcOutput + failureOutput
        };
    }

    private static string ParseStartupType(string qcOutput)
    {
        foreach (var line in qcOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.Contains("START_TYPE", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = line.Split(':', 2);
            if (parts.Length == 2)
            {
                return parts[1].Trim();
            }
        }

        return string.Empty;
    }
}
