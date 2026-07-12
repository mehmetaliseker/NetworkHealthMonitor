using System.Diagnostics;

namespace NetworkHealthMonitor.Services;

public sealed class WindowsServiceStatusService : IWindowsServiceStatusService
{
    public const string ServiceName = "NetworkHealthMonitorWorker";

    public async Task<WindowsServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"query \"{ServiceName}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                return output.Contains("1060", StringComparison.OrdinalIgnoreCase) || error.Contains("1060", StringComparison.OrdinalIgnoreCase)
                    ? new WindowsServiceStatus("NotFound", "Bulunamadı")
                    : new WindowsServiceStatus("Inaccessible", "Erişilemiyor");
            }

            return ParseStatus(output);
        }
        catch
        {
            return new WindowsServiceStatus("Inaccessible", "Erişilemiyor");
        }
    }

    private static WindowsServiceStatus ParseStatus(string output)
    {
        if (output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
        {
            return new WindowsServiceStatus("Running", "Çalışıyor");
        }

        if (output.Contains("START_PENDING", StringComparison.OrdinalIgnoreCase))
        {
            return new WindowsServiceStatus("StartPending", "Başlatılıyor");
        }

        if (output.Contains("STOP_PENDING", StringComparison.OrdinalIgnoreCase))
        {
            return new WindowsServiceStatus("StopPending", "Durduruluyor");
        }

        if (output.Contains("STOPPED", StringComparison.OrdinalIgnoreCase))
        {
            return new WindowsServiceStatus("Stopped", "Durduruldu");
        }

        return WindowsServiceStatus.Unknown("Erişilemiyor");
    }
}
