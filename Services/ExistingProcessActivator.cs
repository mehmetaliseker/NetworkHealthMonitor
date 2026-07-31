using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NetworkHealthMonitor.Services;

public static class ExistingProcessActivator
{
    private const int ShowWindowRestore = 9;

    public static bool ActivateMainWindow(string processName, int currentProcessId)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                if (process.Id == currentProcessId)
                {
                    continue;
                }

                try
                {
                    if (process.MainWindowHandle == IntPtr.Zero)
                    {
                        continue;
                    }

                    ShowWindow(process.MainWindowHandle, ShowWindowRestore);
                    return SetForegroundWindow(process.MainWindowHandle);
                }
                catch (InvalidOperationException)
                {
                    // Process exited while being inspected.
                }
            }
        }

        return false;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
