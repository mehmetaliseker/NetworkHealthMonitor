using System.Windows;
using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Services;
using NetworkHealthMonitor.ViewModels;

namespace NetworkHealthMonitor;

public partial class MainWindow : Window
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly MainViewModel _viewModel;
    private bool _loaded;

    public MainWindow()
    {
        InitializeComponent();

        _connectionFactory = new SqliteConnectionFactory();
        var deviceRepository = new DeviceRepository(_connectionFactory);
        var pingLogRepository = new PingLogRepository(_connectionFactory);

        _viewModel = new MainViewModel(
            deviceRepository,
            pingLogRepository,
            new PingService(),
            new CsvExportService(),
            new AppSettingsService(),
            new WpfDialogService(),
            new DataMaintenanceService(_connectionFactory));

        DataContext = _viewModel;
        Loaded += MainWindowLoaded;
    }

    private async void MainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;

        try
        {
            await _connectionFactory.InitializeAsync();
            await _viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Uygulama başlatılamadı.\n\n{ex.Message}",
                "Başlatma hatası",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
