using System.ComponentModel;
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
    private bool _closingHandled;

    public MainWindow()
    {
        InitializeComponent();

        _connectionFactory = new SqliteConnectionFactory();
        var deviceRepository = new DeviceRepository(_connectionFactory);
        var deviceGroupRepository = new DeviceGroupRepository(_connectionFactory);
        var pingLogRepository = new PingLogRepository(_connectionFactory);
        var schedulePlanRepository = new SchedulePlanRepository(_connectionFactory);
        var outageRepository = new OutageRepository(_connectionFactory);
        var pingService = new PingService();
        var schedulePlanTargetResolver = new SchedulePlanTargetResolver();
        var deviceCheckPolicyService = new DeviceCheckPolicyService();
        var deviceHealthEvaluator = new DeviceHealthEvaluator();
        var appSettingsService = new AppSettingsService();
        var csvExportService = new CsvExportService();
        var deviceImportExportService = new DeviceImportExportService(csvExportService);
        var pingExecutionService = new PingExecutionService(
            deviceRepository,
            deviceGroupRepository,
            pingLogRepository,
            outageRepository,
            pingService,
            deviceCheckPolicyService,
            deviceHealthEvaluator,
            appSettingsService);
        var schedulerService = new SchedulerService(
            deviceRepository,
            deviceGroupRepository,
            schedulePlanRepository,
            pingExecutionService,
            schedulePlanTargetResolver,
            deviceCheckPolicyService,
            appSettingsService);

        _viewModel = new MainViewModel(
            deviceRepository,
            deviceGroupRepository,
            pingLogRepository,
            schedulePlanRepository,
            outageRepository,
            new DeviceService(deviceRepository),
            new DeviceGroupService(deviceGroupRepository),
            new SchedulePlanService(schedulePlanRepository),
            pingExecutionService,
            new AvailabilityService(deviceRepository, pingLogRepository, outageRepository),
            schedulerService,
            schedulePlanTargetResolver,
            csvExportService,
            deviceImportExportService,
            deviceCheckPolicyService,
            appSettingsService,
            new WpfDialogService(),
            new DataMaintenanceService(_connectionFactory));

        DataContext = _viewModel;
        Loaded += MainWindowLoaded;
        Closing += MainWindowClosing;
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

    private async void MainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_closingHandled)
        {
            return;
        }

        e.Cancel = true;
        _closingHandled = true;
        await _viewModel.DisposeAsync();
        Close();
    }
}
