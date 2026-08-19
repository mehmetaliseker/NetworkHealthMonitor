using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace NetworkHealthMonitor.Views.Dialogs;

public partial class AddEditDeviceDialog : System.Windows.Controls.UserControl
{
    public AddEditDeviceDialog()
    {
        InitializeComponent();
        Loaded += OnDialogLoaded;
        IsVisibleChanged += OnDialogIsVisibleChanged;
        PreviewKeyDown += OnDialogPreviewKeyDown;
    }

    private void OnDialogLoaded(object sender, RoutedEventArgs e)
    {
        FocusPrimaryField();
    }

    private void OnDialogIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible && IsLoaded)
        {
            FocusPrimaryField();
        }
    }

    private void OnDialogPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                if (DataContext is ViewModels.MainViewModel viewModel
                    && viewModel.CloseDeviceFormCommand.CanExecute(null))
                {
                    viewModel.CloseDeviceFormCommand.Execute(null);
                    e.Handled = true;
                }

                break;
            case Key.Enter:
                if (Keyboard.FocusedElement is System.Windows.Controls.TextBox { AcceptsReturn: true })
                {
                    return;
                }

                if (DataContext is ViewModels.MainViewModel saveViewModel
                    && saveViewModel.SaveDeviceCommand.CanExecute(null))
                {
                    saveViewModel.SaveDeviceCommand.Execute(null);
                    e.Handled = true;
                }

                break;
        }
    }

    private void FocusPrimaryField()
    {
        Dispatcher.BeginInvoke(
            () =>
            {
                if (!IsVisible)
                {
                    return;
                }

                Keyboard.Focus(DeviceNameBox);
                DeviceNameBox.Focus();
                DeviceNameBox.SelectAll();
            },
            DispatcherPriority.Input);
    }
}
