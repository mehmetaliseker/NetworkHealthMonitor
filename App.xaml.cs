using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Infrastructure;
using WpfApplication = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;

namespace NetworkHealthMonitor;

public partial class App : WpfApplication
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ConfigurePathsFromEnvironment();
        ConfigureBindingTrace();
        DispatcherUnhandledException += HandleDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
        TaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;
        base.OnStartup(e);
    }

    private static void ConfigurePathsFromEnvironment()
    {
        var dataDirectory = Environment.GetEnvironmentVariable("NHM_DATA_DIR");
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            return;
        }

        var legacyDirectory = Environment.GetEnvironmentVariable("NHM_LEGACY_DATA_DIR");
        DatabasePaths.Configure(new FixedApplicationPathProvider(dataDirectory), legacyDirectory);
    }

    private static void ConfigureBindingTrace()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("NHM_BINDING_TRACE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        Directory.CreateDirectory(DatabasePaths.LogDirectory);
        var tracePath = Path.Combine(DatabasePaths.LogDirectory, "wpf-binding-trace.log");
        File.WriteAllText(tracePath, string.Empty);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        PresentationTraceSources.DataBindingSource.Listeners.Add(new TextWriterTraceListener(tracePath));
        PresentationTraceSources.Refresh();
    }

    private void HandleDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppErrorLogger.Log(e.Exception, "DispatcherUnhandledException");
        WpfMessageBox.Show(
            $"Beklenmeyen bir hata oluştu. Teknik ayrıntılar log dosyasına yazıldı:\n{AppErrorLogger.LogFilePath}",
            "Uygulama hatası",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
        Shutdown(1);
    }

    private static void HandleUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            AppErrorLogger.Log(exception, "UnhandledException");
        }
    }

    private static void HandleUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppErrorLogger.Log(e.Exception, "UnobservedTaskException");
        e.SetObserved();
    }
}
