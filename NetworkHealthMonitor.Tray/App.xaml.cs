using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Infrastructure;
using NetworkHealthMonitor.Services;
using NetworkHealthMonitor.Tray.Services;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;

namespace NetworkHealthMonitor.Tray;

public partial class App : WpfApplication
{
    private readonly IWorkerServiceController _workerServiceController = new WindowsWorkerServiceController();
    private readonly TrayPathResolver _pathResolver = new();
    private readonly TraySettingsService _settingsService = new();
    private readonly ElevatedPowerShellRunner _elevatedPowerShellRunner = new();
    private readonly TrayIconFactory _iconFactory = new();
    private readonly IUiAutostartService _trayAutostartService = new WindowsStartupShortcutService(
        shortcutFileName: "NetworkHealthMonitor.Tray.lnk",
        description: "Network Health Monitor tray controller");

    private SingleInstanceGuard? _singleInstanceGuard;
    private CancellationTokenSource? _shutdownTokenSource;
    private DispatcherTimer? _pollTimer;
    private Forms.NotifyIcon? _notifyIcon;
    private Forms.ToolStripMenuItem? _statusItem;
    private Forms.ToolStripMenuItem? _installWorkerItem;
    private Forms.ToolStripMenuItem? _startWorkerItem;
    private Forms.ToolStripMenuItem? _stopWorkerItem;
    private Forms.ToolStripMenuItem? _restartWorkerItem;
    private WorkerServiceStatus _currentStatus = new(WorkerServiceState.Unknown, "Bilinmiyor");
    private TraySettings _settings = new();
    private bool _refreshInProgress;
    private bool _operationInProgress;

    protected override void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        ConfigurePathsFromEnvironment();
        DatabasePaths.EnsureDirectories();
        DispatcherUnhandledException += HandleDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
        TaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;

        _singleInstanceGuard = new SingleInstanceGuard(SingleInstanceNames.Tray);
        if (!_singleInstanceGuard.IsFirstInstance)
        {
            Shutdown(0);
            return;
        }

        base.OnStartup(e);

        _shutdownTokenSource = new CancellationTokenSource();
        _notifyIcon = CreateNotifyIcon();
        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _pollTimer.Tick += PollTimerTick;
        _pollTimer.Start();

        _ = InitializeAsync(_shutdownTokenSource.Token);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _pollTimer?.Stop();
        if (_pollTimer is not null)
        {
            _pollTimer.Tick -= PollTimerTick;
        }

        _shutdownTokenSource?.Cancel();
        _shutdownTokenSource?.Dispose();
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }

        _iconFactory.Dispose();
        _singleInstanceGuard?.Dispose();
        base.OnExit(e);
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _settings = await _settingsService.LoadAsync(cancellationToken);
        await RefreshStatusAsync(cancellationToken);

        if (!_settings.StartWorkerWhenTrayStarts || _currentStatus.State != WorkerServiceState.Stopped)
        {
            return;
        }

        try
        {
            await StartWorkerAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            AppErrorLogger.Log(ex, "Tray startup worker start failed.");
        }
    }

    private Forms.NotifyIcon CreateNotifyIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Opening += (_, _) => UpdateMenuState();
        menu.Items.Add(new Forms.ToolStripMenuItem("Network Health Monitor") { Enabled = false });
        _statusItem = new Forms.ToolStripMenuItem("Durum: Bilinmiyor") { Enabled = false };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Yönetim Panelini Aç", null, (_, _) => OpenManagementPanel());
        _installWorkerItem = new Forms.ToolStripMenuItem("Worker'ı Kur", null, async (_, _) => await InstallWorkerAsync());
        menu.Items.Add(_installWorkerItem);
        _startWorkerItem = new Forms.ToolStripMenuItem("Worker'ı Başlat", null, async (_, _) => await StartWorkerFromMenuAsync());
        _stopWorkerItem = new Forms.ToolStripMenuItem("Worker'ı Durdur", null, async (_, _) => await StopWorkerFromMenuAsync());
        _restartWorkerItem = new Forms.ToolStripMenuItem("Worker'ı Yeniden Başlat", null, async (_, _) => await RestartWorkerFromMenuAsync());
        menu.Items.Add(_startWorkerItem);
        menu.Items.Add(_stopWorkerItem);
        menu.Items.Add(_restartWorkerItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Son Kontrol Zamanını Göster", null, async (_, _) => await ShowLastControlTimeAsync());
        menu.Items.Add("Log Klasörünü Aç", null, (_, _) => OpenFolder(_pathResolver.LogDirectory));
        menu.Items.Add("Veri Klasörünü Aç", null, (_, _) => OpenFolder(_pathResolver.DataDirectory));
        menu.Items.Add("Servis Yönetimini Aç", null, (_, _) => OpenServiceManagement());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Ayarlar", null, async (_, _) => await ShowSettingsAsync());
        menu.Items.Add("Hakkında", null, (_, _) => ShowAbout());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Tray Uygulamasından Çık", null, (_, _) => ExitTray());

        var icon = new Forms.NotifyIcon
        {
            Icon = _iconFactory.GetIcon(_currentStatus.State),
            Text = "Network Health Monitor - Worker durumu bilinmiyor",
            ContextMenuStrip = menu,
            Visible = true
        };
        icon.DoubleClick += (_, _) => OpenManagementPanel();
        return icon;
    }

    private async void PollTimerTick(object? sender, EventArgs e)
    {
        if (_shutdownTokenSource is null)
        {
            return;
        }

        await RefreshStatusAsync(_shutdownTokenSource.Token);
    }

    private async Task RefreshStatusAsync(CancellationToken cancellationToken)
    {
        if (_refreshInProgress || _operationInProgress)
        {
            return;
        }

        _refreshInProgress = true;
        try
        {
            _currentStatus = await _workerServiceController.GetStatusAsync(cancellationToken);
            UpdateMenuState();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppErrorLogger.Log(ex, "Tray service status refresh failed.");
            _currentStatus = WorkerServiceStatus.Error("Hata", ex.Message);
            UpdateMenuState();
        }
        finally
        {
            _refreshInProgress = false;
        }
    }

    private void UpdateMenuState()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        var state = TrayMenuStateBuilder.Build(_currentStatus);
        _statusItem!.Text = state.StatusText;
        _installWorkerItem!.Visible = state.ShowInstall;
        _installWorkerItem.Enabled = !_operationInProgress;
        _startWorkerItem!.Enabled = state.CanStart && !_operationInProgress;
        _stopWorkerItem!.Enabled = state.CanStop && !_operationInProgress;
        _restartWorkerItem!.Enabled = state.CanRestart && !_operationInProgress;
        _notifyIcon.Text = LimitTooltip(state.ToolTipText);
        _notifyIcon.Icon = _iconFactory.GetIcon(_currentStatus.State);
    }

    private void OpenManagementPanel()
    {
        try
        {
            if (ExistingProcessActivator.ActivateMainWindow("NetworkHealthMonitor", Environment.ProcessId))
            {
                return;
            }

            var uiPath = _pathResolver.ManagementUiPath;
            if (!File.Exists(uiPath))
            {
                ShowUserError("Yönetim paneli açılamadı.", $"UI exe bulunamadı: {uiPath}");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = uiPath,
                WorkingDirectory = Path.GetDirectoryName(uiPath) ?? AppContext.BaseDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppErrorLogger.Log(ex, "Tray open management panel failed.");
            ShowUserError("Yönetim paneli açılamadı.", ex.Message);
        }
    }

    private Task InstallWorkerAsync()
    {
        return RunElevatedScriptAsync(
            "Install-WorkerService.ps1",
            [
                "-WorkerPath",
                _pathResolver.WorkerPath
            ],
            "Worker kurulamadı.");
    }

    private Task StartWorkerFromMenuAsync()
    {
        return RunWorkerOperationAsync(
            WorkerServiceState.StartPending,
            token => _workerServiceController.StartAsync(token),
            () => RunElevatedScriptAsync("Start-Worker.ps1", [], "Worker başlatılamadı."),
            "Worker başlatılamadı.");
    }

    private Task StopWorkerFromMenuAsync()
    {
        return RunWorkerOperationAsync(
            WorkerServiceState.StopPending,
            token => _workerServiceController.StopAsync(token),
            () => RunElevatedScriptAsync("Stop-Worker.ps1", [], "Worker durdurulamadı."),
            "Worker durdurulamadı.");
    }

    private Task RestartWorkerFromMenuAsync()
    {
        return RunWorkerOperationAsync(
            WorkerServiceState.StartPending,
            token => _workerServiceController.RestartAsync(token),
            () => RunElevatedScriptAsync("Restart-Worker.ps1", [], "Worker yeniden başlatılamadı."),
            "Worker yeniden başlatılamadı.");
    }

    private async Task StartWorkerAsync(CancellationToken cancellationToken)
    {
        await RunWorkerOperationAsync(
            WorkerServiceState.StartPending,
            token => _workerServiceController.StartAsync(token),
            () => RunElevatedScriptAsync("Start-Worker.ps1", [], "Worker başlatılamadı."),
            "Worker başlatılamadı.",
            cancellationToken);
    }

    private async Task RunWorkerOperationAsync(
        WorkerServiceState pendingState,
        Func<CancellationToken, Task> operation,
        Func<Task> elevatedFallback,
        string failureTitle,
        CancellationToken cancellationToken = default)
    {
        _operationInProgress = true;
        _currentStatus = new WorkerServiceStatus(pendingState, pendingState == WorkerServiceState.StopPending ? "Durduruluyor" : "Başlatılıyor");
        UpdateMenuState();

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            await operation(timeout.Token);
        }
        catch (WorkerServiceControlException ex) when (ex.Error == WorkerServiceControlError.AccessDenied)
        {
            AppErrorLogger.Log(ex, failureTitle);
            if (Confirm("Yönetici yetkisi gerekiyor", $"{failureTitle}\n\nİşlem UAC ile yönetici olarak denensin mi?"))
            {
                await elevatedFallback();
            }
        }
        catch (Exception ex)
        {
            AppErrorLogger.Log(ex, failureTitle);
            ShowUserError(failureTitle, BuildServiceErrorMessage(ex));
        }
        finally
        {
            _operationInProgress = false;
            if (_shutdownTokenSource is not null)
            {
                await RefreshStatusAsync(_shutdownTokenSource.Token);
            }
        }
    }

    private async Task RunElevatedScriptAsync(string scriptName, IReadOnlyList<string> arguments, string failureTitle)
    {
        var scriptPath = _pathResolver.ResolveScriptPath(scriptName);
        var ok = await _elevatedPowerShellRunner.RunScriptAsync(scriptPath, arguments);
        if (!ok)
        {
            ShowUserError(failureTitle, "İşlem tamamlanmadı. UAC iptal edilmiş veya script hata ile bitmiş olabilir.");
        }

        if (_shutdownTokenSource is not null)
        {
            await RefreshStatusAsync(_shutdownTokenSource.Token);
        }
    }

    private async Task ShowLastControlTimeAsync()
    {
        try
        {
            var factory = new SqliteConnectionFactory();
            await factory.InitializeAsync();
            var heartbeat = await new WorkerHeartbeatRepository(factory).GetLatestAsync();
            if (heartbeat is null)
            {
                WpfMessageBox.Show(
                    "Henüz Worker heartbeat kaydı yok.",
                    "Son Kontrol Zamanı",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var lines = string.Join(
                Environment.NewLine,
                $"Son heartbeat: {FormatLocal(heartbeat.LastSeenAtUtc)}",
                $"Son scheduler çevrimi: {FormatLocal(heartbeat.LastSchedulerCycleAtUtc)}",
                $"Son başarılı ping: {FormatLocal(heartbeat.LastSuccessfulPingAtUtc)}",
                $"Son bildirim gönderimi: {FormatLocal(heartbeat.LastNotificationDispatchAtUtc)}");
            WpfMessageBox.Show(lines, "Son Kontrol Zamanı", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppErrorLogger.Log(ex, "Tray show last control time failed.");
            ShowUserError("Son kontrol zamanı okunamadı.", ex.Message);
        }
    }

    private async Task ShowSettingsAsync()
    {
        try
        {
            var window = new SettingsWindow(_settings);
            if (window.ShowDialog() != true)
            {
                return;
            }

            _settings = window.Settings;
            await _settingsService.SaveAsync(_settings);
            await _trayAutostartService.SetEnabledAsync(
                _settings.StartTrayOnWindowsLogin,
                Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? AppContext.BaseDirectory);
        }
        catch (Exception ex)
        {
            AppErrorLogger.Log(ex, "Tray settings failed.");
            ShowUserError("Ayarlar kaydedilemedi.", ex.Message);
        }
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppErrorLogger.Log(ex, "Tray open folder failed.");
            ShowUserError("Klasör açılamadı.", ex.Message);
        }
    }

    private static void OpenServiceManagement()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "services.msc",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppErrorLogger.Log(ex, "Tray open services management failed.");
            ShowUserError("Servis yönetimi açılamadı.", ex.Message);
        }
    }

    private static void ShowAbout()
    {
        WpfMessageBox.Show(
            "Network Health Monitor Tray\nWorker hizmetini kullanıcı oturumundan manuel yönetir.",
            "Hakkında",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ExitTray()
    {
        if (_currentStatus.State == WorkerServiceState.Running)
        {
            WpfMessageBox.Show(
                "Tray uygulaması kapanacak. Worker hizmeti çalışmaya devam edecek.",
                "Tray Uygulamasından Çık",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        Shutdown(0);
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

    private static string BuildServiceErrorMessage(Exception exception)
    {
        if (exception is WorkerServiceControlException serviceException)
        {
            return serviceException.Message;
        }

        return "Windows hizmeti bulunamadı veya gerekli yetki mevcut değil.";
    }

    private static string FormatLocal(DateTime? value)
    {
        return value.HasValue ? value.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss") : "-";
    }

    private static string LimitTooltip(string value)
    {
        return value.Length <= 63 ? value : value[..63];
    }

    private static bool Confirm(string title, string message)
    {
        return WpfMessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    private static void ShowUserError(string title, string message)
    {
        WpfMessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void HandleDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppErrorLogger.Log(e.Exception, "Tray DispatcherUnhandledException");
        ShowUserError("Tray hatası", $"Teknik ayrıntılar log dosyasına yazıldı:\n{AppErrorLogger.LogFilePath}");
        e.Handled = true;
    }

    private static void HandleUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            AppErrorLogger.Log(exception, "Tray UnhandledException");
        }
    }

    private static void HandleUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppErrorLogger.Log(e.Exception, "Tray UnobservedTaskException");
        e.SetObserved();
    }
}
