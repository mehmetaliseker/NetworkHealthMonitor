using System.Windows;
using System.Windows.Threading;

namespace NetworkHealthMonitor;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += HandleDispatcherUnhandledException;
        base.OnStartup(e);
    }

    private static void HandleDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Beklenmeyen bir hata oluştu.\n\n{e.Exception.Message}",
            "Uygulama hatası",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }
}
