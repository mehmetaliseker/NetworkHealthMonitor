using System.ComponentModel;
using System.Diagnostics;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class WindowsServiceStatusService : IWindowsServiceStatusService
{
    public const string ServiceName = WorkerServiceConstants.ServiceName;

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

    public async Task<OperationResult> SetStartupTypeAsync(bool startWithWindows, CancellationToken cancellationToken = default)
    {
        var desiredScValue = startWithWindows ? "auto" : "demand";
        var desiredPowerShellValue = startWithWindows ? "Automatic" : "Manual";
        var direct = await RunScAsync($"config \"{ServiceName}\" start= {desiredScValue}", cancellationToken);
        if (direct.ExitCode != 0)
        {
            if (!IsAccessDenied(direct))
            {
                return OperationResult.Fail(BuildFailureMessage("Worker başlangıç tipi değiştirilemedi.", direct));
            }

            var elevated = await RunElevatedPowerShellAsync(
                $"Set-Service -Name '{ServiceName}' -StartupType {desiredPowerShellValue}",
                cancellationToken);
            if (!elevated.Success)
            {
                return elevated;
            }
        }

        var status = await GetStatusAsync(cancellationToken);
        if (startWithWindows && !status.IsAutomaticStartup)
        {
            return OperationResult.Fail("Worker otomatik başlangıç ayarı doğrulanamadı.");
        }

        if (!startWithWindows && !status.IsManualStartup)
        {
            return OperationResult.Fail("Worker manuel başlangıç ayarı doğrulanamadı.");
        }

        return OperationResult.Ok(startWithWindows
            ? "Worker, Windows açıldığında otomatik başlayacak."
            : "Worker otomatik başlangıcı kapatıldı. Bilgisayar açıldığında manuel olarak başlatılacak.");
    }

    public async Task<OperationResult> StartAsync(CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(cancellationToken);
        if (!status.IsInstalled)
        {
            return OperationResult.Fail("Worker servisi kurulu değil.");
        }

        if (status.Code == "Running")
        {
            return OperationResult.Ok("Worker zaten çalışıyor.");
        }

        return await RunServiceCommandAsync(
            $"start \"{ServiceName}\"",
            $"Start-Service -Name '{ServiceName}'",
            "Worker başlatıldı.",
            "Worker başlatılamadı.",
            cancellationToken);
    }

    public async Task<OperationResult> StopAsync(CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(cancellationToken);
        if (!status.IsInstalled)
        {
            return OperationResult.Fail("Worker servisi kurulu değil.");
        }

        if (status.Code == "Stopped")
        {
            return OperationResult.Ok("Worker zaten durdurulmuş.");
        }

        return await RunServiceCommandAsync(
            $"stop \"{ServiceName}\"",
            $"Stop-Service -Name '{ServiceName}'",
            "Worker durduruldu.",
            "Worker durdurulamadı.",
            cancellationToken);
    }

    public async Task<OperationResult> RestartAsync(CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(cancellationToken);
        if (!status.IsInstalled)
        {
            return OperationResult.Fail("Worker servisi kurulu değil.");
        }

        if (status.Code == "Running")
        {
            var stop = await StopAsync(cancellationToken);
            if (!stop.Success)
            {
                return stop;
            }
        }

        var start = await StartAsync(cancellationToken);
        return start.Success
            ? OperationResult.Ok("Worker yeniden başlatıldı.")
            : start;
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

    private static async Task<OperationResult> RunServiceCommandAsync(
        string scArguments,
        string elevatedCommand,
        string successMessage,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        var direct = await RunScAsync(scArguments, cancellationToken);
        if (direct.ExitCode == 0)
        {
            return OperationResult.Ok(successMessage);
        }

        if (!IsAccessDenied(direct))
        {
            return OperationResult.Fail(BuildFailureMessage(failureMessage, direct));
        }

        var elevated = await RunElevatedPowerShellAsync(elevatedCommand, cancellationToken);
        return elevated.Success
            ? OperationResult.Ok(successMessage)
            : elevated;
    }

    private static async Task<OperationResult> RunElevatedPowerShellAsync(string command, CancellationToken cancellationToken)
    {
        try
        {
            var escaped = command.Replace("\"", "\\\"", StringComparison.Ordinal);
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{escaped}\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            });

            if (process is null)
            {
                return OperationResult.Fail("Yönetici işlemi başlatılamadı.");
            }

            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0
                ? OperationResult.Ok()
                : OperationResult.Fail("İşlem tamamlanmadı. UAC iptal edilmiş veya komut hata ile bitmiş olabilir.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return OperationResult.Fail("İşlem iptal edildi. Yönetici onayı verilmedi.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Yönetici işlemi başlatılamadı: {ex.Message}");
        }
    }

    private static bool IsAccessDenied((int ExitCode, string Output, string Error) result)
    {
        return result.ExitCode == 5
               || result.Output.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)
               || result.Error.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)
               || result.Output.Contains("Erişim engellendi", StringComparison.OrdinalIgnoreCase)
               || result.Error.Contains("Erişim engellendi", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildFailureMessage(string prefix, (int ExitCode, string Output, string Error) result)
    {
        var detail = string.Join(" ", new[] { result.Output.Trim(), result.Error.Trim() }.Where(item => !string.IsNullOrWhiteSpace(item)));
        return string.IsNullOrWhiteSpace(detail)
            ? $"{prefix} sc.exe çıkış kodu: {result.ExitCode}."
            : $"{prefix} {detail}";
    }

    public static WindowsServiceStatus ParseStatus(string queryOutput, string qcOutput, string failureOutput)
    {
        var startupType = ParseStartupType(qcOutput);
        var isAutomatic = startupType.Contains("AUTO", StringComparison.OrdinalIgnoreCase);
        var isManual = startupType.Contains("DEMAND", StringComparison.OrdinalIgnoreCase)
                       || startupType.Contains("MANUAL", StringComparison.OrdinalIgnoreCase);
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
            IsManualStartup = isManual,
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
