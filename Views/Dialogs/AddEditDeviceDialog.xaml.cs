using System.Windows;

namespace NetworkHealthMonitor.Views.Dialogs;

public partial class AddEditDeviceDialog : System.Windows.Controls.UserControl
{
    public AddEditDeviceDialog()
    {
        InitializeComponent();
        Loaded += AddEditDeviceDialogLoaded;
    }

    private void AddEditDeviceDialogLoaded(object sender, RoutedEventArgs e)
    {
        DeviceNameBox.Focus();
        DeviceNameBox.SelectAll();
    }
}
