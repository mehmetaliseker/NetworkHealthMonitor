using System.ComponentModel;
using System.Drawing;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Infrastructure;
using NetworkHealthMonitor.Services;
using NetworkHealthMonitor.ViewModels;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;

namespace NetworkHealthMonitor;

public partial class MainWindow : Window
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly MainViewModel _viewModel;
    private readonly Forms.NotifyIcon _notifyIcon;
    private bool _loaded;
    private bool _closingHandled;
    private bool _exitRequested;

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
        var alertPolicyService = new AlertPolicyService();
        var notificationOutboxRepository = new NotificationOutboxRepository(_connectionFactory);
        var heartbeatRepository = new WorkerHeartbeatRepository(_connectionFactory);
        var notificationClient = new NtfyNotificationClient(new DefaultHttpClientFactory(), new DpapiSecretProtector());
        var notificationPublisher = new NotificationPublisher(notificationOutboxRepository);
        var incidentService = new IncidentService(_connectionFactory, appSettingsService, alertPolicyService);
        var availabilityRepository = new AvailabilityRepository(_connectionFactory);
        var csvExportService = new CsvExportService();
        var maintenanceService = new DataMaintenanceService(_connectionFactory);
        var deviceImportExportService = new DeviceImportExportService(csvExportService, deviceRepository, maintenanceService);
        var pingExecutionService = new PingExecutionService(
            deviceRepository,
            deviceGroupRepository,
            pingLogRepository,
            outageRepository,
            pingService,
            deviceCheckPolicyService,
            deviceHealthEvaluator,
            appSettingsService,
            incidentService,
            null,
            availabilityRepository,
            $"UI-{Environment.ProcessId}");
        var schedulerService = new SchedulerService(
            deviceRepository,
            deviceGroupRepository,
            schedulePlanRepository,
            pingLogRepository,
            pingExecutionService,
            schedulePlanTargetResolver,
            deviceCheckPolicyService,
            appSettingsService,
            availabilityRepository: availabilityRepository);

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
            new AvailabilityService(availabilityRepository),
            schedulerService,
            schedulePlanTargetResolver,
            csvExportService,
            deviceImportExportService,
            deviceCheckPolicyService,
            appSettingsService,
            new WpfDialogService(),
            maintenanceService,
            null,
            notificationPublisher,
            notificationClient,
            notificationOutboxRepository,
            heartbeatRepository,
            new WindowsStartupShortcutService());

        DataContext = _viewModel;
        Loaded += MainWindowLoaded;
        Closing += MainWindowClosing;
        StateChanged += MainWindowStateChanged;

        _notifyIcon = CreateNotifyIcon();
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
            AppErrorLogger.Log(ex, "Startup");
            if (!string.IsNullOrWhiteSpace(DatabasePaths.DatabaseFilePath))
            {
                WpfMessageBox.Show(
                    BuildStartupErrorMessage(ex),
                    "Baslatma hatasi",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                WpfApplication.Current.Shutdown(1);
            }
            else
            {
            WpfMessageBox.Show(
                $"Uygulama başlatılamadı. Teknik ayrıntılar log dosyasına yazıldı:\n{AppErrorLogger.LogFilePath}",
                "Başlatma hatası",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
                WpfApplication.Current.Shutdown(1);
            }
        }
    }

    private static string BuildStartupErrorMessage(Exception exception)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        return string.Join(
            Environment.NewLine,
            "Uygulama baslatilamadi.",
            $"DB yolu: {DatabasePaths.DatabaseFilePath}",
            $"Log yolu: {AppErrorLogger.LogFilePath}",
            $"Uygulama surumu: {version}",
            $"Hata ozeti: {exception.GetType().Name}: {exception.Message}");
    }

    private async void MainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_closingHandled)
        {
            return;
        }

        e.Cancel = true;
        _closingHandled = true;

        try
        {
            if (!_exitRequested && !string.Equals(Environment.GetEnvironmentVariable("NHM_SUPPRESS_CLOSE_NOTICE"), "1", StringComparison.Ordinal))
            {
                WpfMessageBox.Show(
                    "Arayüz kapanacak. Network Health Monitor Worker servisi kurulu ve çalışıyorsa izleme arka planda devam eder.",
                    "Arayüz kapatılıyor",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            await _viewModel.DisposeAsync();
        }
        catch (Exception ex)
        {
            AppErrorLogger.Log(ex, "Shutdown");
        }
        finally
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            Closing -= MainWindowClosing;
            _ = Dispatcher.BeginInvoke(Close, DispatcherPriority.Background);
        }
    }

    private Forms.NotifyIcon CreateNotifyIcon()
    {
        var contextMenu = new Forms.ContextMenuStrip();
        contextMenu.Items.Add("Uygulamayı Aç", null, (_, _) => ShowFromTray());
        contextMenu.Items.Add("Servis Durumu", null, async (_, _) => await ShowServiceStatusAsync());
        contextMenu.Items.Add("Son Kontrol Özeti", null, (_, _) => ShowLastSummary());
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add("Arayüzden Çık", null, (_, _) => ExitFromTray());

        var notifyIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Ağ Cihazları Kontrol Paneli",
            ContextMenuStrip = contextMenu,
            Visible = true
        };
        notifyIcon.DoubleClick += (_, _) => ShowFromTray();
        return notifyIcon;
    }

    private void MainWindowStateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized)
        {
            return;
        }

        Hide();
        _notifyIcon.BalloonTipTitle = "Ağ Cihazları Kontrol Paneli";
        _notifyIcon.BalloonTipText = "Arayüz sistem tepsisine alındı. İzleme servisi ayrı çalışır.";
        _notifyIcon.ShowBalloonTip(2500);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private async Task ShowServiceStatusAsync()
    {
        var status = await _viewModel.GetWorkerServiceStatusTextAsync();
        WpfMessageBox.Show($"Network Health Monitor Worker durumu: {status}", "Servis Durumu", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ShowLastSummary()
    {
        WpfMessageBox.Show(_viewModel.StatusMessage, "Son Kontrol Özeti", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ExitFromTray()
    {
        _exitRequested = true;
        Close();
    }
}
