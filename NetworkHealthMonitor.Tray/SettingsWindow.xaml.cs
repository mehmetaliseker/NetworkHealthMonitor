using System.Windows;
using NetworkHealthMonitor.Tray.Services;

namespace NetworkHealthMonitor.Tray;

public partial class SettingsWindow : Window
{
    public SettingsWindow(TraySettings settings)
    {
        InitializeComponent();
        StartTrayOnWindowsLoginCheckBox.IsChecked = settings.StartTrayOnWindowsLogin;
        StartWorkerWhenTrayStartsCheckBox.IsChecked = settings.StartWorkerWhenTrayStarts;
    }

    public TraySettings Settings { get; private set; } = new();

    private void SaveClicked(object sender, RoutedEventArgs e)
    {
        Settings = new TraySettings
        {
            StartTrayOnWindowsLogin = StartTrayOnWindowsLoginCheckBox.IsChecked == true,
            StartWorkerWhenTrayStarts = StartWorkerWhenTrayStartsCheckBox.IsChecked == true
        };
        DialogResult = true;
    }
}
