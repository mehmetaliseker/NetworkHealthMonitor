using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace NetworkHealthMonitor.Tray.Services;

public sealed class ElevatedPowerShellRunner
{
    private const int OperationCanceledByUser = 1223;

    public Task<bool> RunScriptAsync(string scriptPath, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("Script bulunamadı.", scriptPath);
        }

        return Task.Run(() =>
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            process.StartInfo.ArgumentList.Add("-NoProfile");
            process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
            process.StartInfo.ArgumentList.Add("Bypass");
            process.StartInfo.ArgumentList.Add("-File");
            process.StartInfo.ArgumentList.Add(scriptPath);
            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            try
            {
                if (!process.Start())
                {
                    return false;
                }

                process.WaitForExit();
                cancellationToken.ThrowIfCancellationRequested();
                return process.ExitCode == 0;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == OperationCanceledByUser)
            {
                return false;
            }
        }, cancellationToken);
    }
}
