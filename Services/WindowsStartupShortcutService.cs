using System.IO;
using System.Reflection;

namespace NetworkHealthMonitor.Services;

public sealed class WindowsStartupShortcutService : IUiAutostartService
{
    private const string ShortcutFileName = "NetworkHealthMonitor.lnk";
    private readonly string _startupDirectory;

    public WindowsStartupShortcutService(string? startupDirectory = null)
    {
        _startupDirectory = string.IsNullOrWhiteSpace(startupDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft",
                "Windows",
                "Start Menu",
                "Programs",
                "Startup")
            : startupDirectory;
    }

    public string ShortcutPath => Path.Combine(_startupDirectory, ShortcutFileName);

    public bool IsEnabled(string targetPath)
    {
        if (!File.Exists(ShortcutPath))
        {
            return false;
        }

        try
        {
            var shortcut = CreateShortcutObject(ShortcutPath);
            var currentTarget = GetProperty(shortcut, "TargetPath");
            return PathsEqual(currentTarget, targetPath) && File.Exists(currentTarget);
        }
        catch
        {
            return false;
        }
    }

    public Task SetEnabledAsync(bool enabled, string targetPath)
    {
        if (enabled)
        {
            CreateOrUpdateShortcut(targetPath);
        }
        else
        {
            RemoveShortcut();
        }

        return Task.CompletedTask;
    }

    private void CreateOrUpdateShortcut(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new InvalidOperationException("UI executable path could not be resolved.");
        }

        Directory.CreateDirectory(_startupDirectory);
        if (File.Exists(ShortcutPath))
        {
            try
            {
                var existing = CreateShortcutObject(ShortcutPath);
                var existingTarget = GetProperty(existing, "TargetPath");
                if (!PathsEqual(existingTarget, targetPath) || !File.Exists(existingTarget))
                {
                    File.Delete(ShortcutPath);
                }
            }
            catch
            {
                File.Delete(ShortcutPath);
            }
        }

        var shortcut = CreateShortcutObject(ShortcutPath);
        SetProperty(shortcut, "TargetPath", targetPath);
        SetProperty(shortcut, "WorkingDirectory", Path.GetDirectoryName(targetPath) ?? AppContext.BaseDirectory);
        SetProperty(shortcut, "Description", "Network Health Monitor management UI");
        Invoke(shortcut, "Save");
    }

    private void RemoveShortcut()
    {
        if (File.Exists(ShortcutPath))
        {
            File.Delete(ShortcutPath);
        }
    }

    private static object CreateShortcutObject(string shortcutPath)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell COM component is not available.");
        var shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("WScript.Shell could not be created.");
        return shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath })
            ?? throw new InvalidOperationException("Startup shortcut could not be created.");
    }

    private static string GetProperty(object target, string propertyName)
    {
        return Convert.ToString(target.GetType().InvokeMember(propertyName, BindingFlags.GetProperty, null, target, Array.Empty<object>())) ?? string.Empty;
    }

    private static void SetProperty(object target, string propertyName, object value)
    {
        target.GetType().InvokeMember(propertyName, BindingFlags.SetProperty, null, target, new[] { value });
    }

    private static void Invoke(object target, string methodName)
    {
        target.GetType().InvokeMember(methodName, BindingFlags.InvokeMethod, null, target, Array.Empty<object>());
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }
}
