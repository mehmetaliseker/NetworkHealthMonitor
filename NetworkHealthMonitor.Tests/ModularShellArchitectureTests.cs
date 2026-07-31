using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Models;
using NetworkHealthMonitor.Services;
using NetworkHealthMonitor.ViewModels;
using NetworkHealthMonitor.ViewModels.Dashboard;
using NetworkHealthMonitor.ViewModels.Devices;
using NetworkHealthMonitor.ViewModels.Navigation;
using NetworkHealthMonitor.ViewModels.Pages;
using NetworkHealthMonitor.ViewModels.Settings;
using NetworkHealthMonitor.ViewModels.Shell;
using NetworkHealthMonitor.ViewModels.Worker;
using System.Windows.Threading;
using Xunit;

namespace NetworkHealthMonitor.Tests;

public sealed class ModularShellArchitectureTests
{
    [Fact]
    public Task Shell_routes_to_distinct_page_view_models_and_builds_breadcrumb()
    {
        return RunOnStaAsync(async () =>
        {
            await using var store = await TestStore.CreateAsync();
            var repository = new DeviceRepository(store.ConnectionFactory);
            var device = new Device
            {
                Name = "Moduler Test Cihaz",
                IpAddress = "192.0.2.77",
                DeviceType = DeviceType.Switch,
                IsActive = true,
                IsEnabled = true,
                AutoCheckEnabled = true,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            device.Id = await repository.AddAsync(device);

            var legacy = await CreateViewModelAsync(store);
            await using var shell = new ShellViewModel(legacy);

            await shell.InitializeAsync();
            Assert.IsType<DashboardViewModel>(shell.CurrentPage);
            Assert.True(shell.IsDashboardNavSelected);
            Assert.Equal("Genel Bakış", shell.PageTitle);

            await shell.NavigateToAsync<DevicesViewModel>();
            Assert.IsType<DevicesViewModel>(shell.CurrentPage);
            Assert.True(shell.IsDevicesNavSelected);

            await shell.NavigateToAsync<SettingsViewModel>();
            Assert.IsType<SettingsViewModel>(shell.CurrentPage);
            Assert.True(shell.IsSettingsNavSelected);

            var loadedDevice = Assert.Single(legacy.Devices);
            await shell.NavigateToAsync<DeviceDetailsViewModel>(loadedDevice);

            Assert.IsType<DeviceDetailsViewModel>(shell.CurrentPage);
            Assert.True(shell.IsDevicesNavSelected);
            Assert.Equal("Cihazlar > Moduler Test Cihaz", shell.BreadcrumbText);
            Assert.True(shell.CanGoBack);

            await shell.NavigationService.GoBackAsync();
            Assert.IsType<SettingsViewModel>(shell.CurrentPage);

            await legacy.DisposeAsync();
        });
    }

    [Fact]
    public Task Cached_pages_do_not_reload_on_simple_return_navigation()
    {
        return RunOnStaAsync(async () =>
        {
            await using var store = await TestStore.CreateAsync();
            var legacy = await CreateViewModelAsync(store);
            await using var shell = new ShellViewModel(legacy);

            await shell.InitializeAsync();
            Assert.Equal(1, shell.DashboardPage.LoadCount);

            await shell.NavigateToAsync<DevicesViewModel>();
            await shell.NavigateToAsync<DashboardViewModel>();

            Assert.Equal(1, shell.DashboardPage.LoadCount);
            Assert.Equal(1, shell.DevicesPage.LoadCount);

            await legacy.DisposeAsync();
        });
    }

    [Fact]
    public async Task Page_lifecycle_exposes_loading_empty_error_retry_and_cancellation()
    {
        var page = new ProbePage();

        await page.OnNavigatedToAsync(new NavigationContext("probe"), CancellationToken.None);

        Assert.False(page.IsLoading);
        Assert.True(page.HasData);
        Assert.False(page.HasError);
        Assert.Equal(1, page.LoadCount);

        await page.OnNavigatedToAsync(new NavigationContext("probe"), CancellationToken.None);
        Assert.Equal(1, page.LoadCount);

        await page.RefreshAsync(CancellationToken.None);
        Assert.Equal(2, page.LoadCount);

        page.FailOnLoad = true;
        await page.RefreshAsync(CancellationToken.None);
        Assert.True(page.HasError);
        Assert.False(page.HasData);

        await page.OnNavigatedFromAsync(CancellationToken.None);
        Assert.True(page.NavigatedFromCalled);
    }

    [Fact]
    public void MainWindow_is_shell_and_required_views_exist()
    {
        var root = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(root, "NetworkHealthMonitor.sln")))
        {
            root = Directory.GetParent(root)!.FullName;
        }

        var mainWindow = File.ReadAllText(Path.Combine(root, "MainWindow.xaml"));
        Assert.Contains("shellViews:MainShellView", mainWindow);
        Assert.DoesNotContain("<DataGrid", mainWindow, StringComparison.OrdinalIgnoreCase);

        var requiredViews = new[]
        {
            "Views/Shell/MainShellView.xaml",
            "Views/Dashboard/DashboardView.xaml",
            "Views/Devices/DevicesView.xaml",
            "Views/Devices/DeviceDetailsView.xaml",
            "Views/Devices/DeviceGroupsView.xaml",
            "Views/LiveStatus/LiveStatusView.xaml",
            "Views/Schedules/SchedulesView.xaml",
            "Views/PingHistory/PingHistoryView.xaml",
            "Views/Incidents/IncidentsView.xaml",
            "Views/Notifications/NotificationsView.xaml",
            "Views/Reports/ReportsView.xaml",
            "Views/Worker/WorkerServiceView.xaml",
            "Views/SystemHealth/SystemHealthView.xaml",
            "Views/Settings/SettingsView.xaml",
            "Views/Help/HelpView.xaml",
            "Views/About/AboutView.xaml"
        };

        foreach (var relativePath in requiredViews)
        {
            Assert.True(File.Exists(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar))), relativePath);
        }
    }

    [Fact]
    public void Device_row_actions_pass_the_row_device_as_command_parameter()
    {
        var root = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(root, "NetworkHealthMonitor.sln")))
        {
            root = Directory.GetParent(root)!.FullName;
        }

        var devicesView = File.ReadAllText(Path.Combine(root, "Views", "Devices", "DevicesView.xaml"));

        Assert.Contains("CommandParameter=\"{Binding}\"", devicesView);
        Assert.DoesNotContain("CommandParameter=\"{Binding DataContext, RelativeSource={RelativeSource AncestorType=DataGridRow}}\"", devicesView);
    }

    private static async Task<MainViewModel> CreateViewModelAsync(TestStore store)
    {
        await new AppSettingsService().SaveAsync(AppSettings.Default);
        var devices = new DeviceRepository(store.ConnectionFactory);
        var groups = new DeviceGroupRepository(store.ConnectionFactory);
        var logs = new PingLogRepository(store.ConnectionFactory);
        var schedules = new SchedulePlanRepository(store.ConnectionFactory);
        var outages = new OutageRepository(store.ConnectionFactory);
        var checkPolicy = new DeviceCheckPolicyService();
        var settings = new AppSettingsService();
        var maintenance = new DataMaintenanceService(store.ConnectionFactory);
        var pingExecution = new PingExecutionService(
            devices,
            groups,
            logs,
            outages,
            new FakePingService(true),
            checkPolicy,
            new DeviceHealthEvaluator(),
            settings);

        var vm = new MainViewModel(
            devices,
            groups,
            logs,
            schedules,
            outages,
            new DeviceService(devices),
            new DeviceGroupService(groups),
            new SchedulePlanService(schedules),
            pingExecution,
            new AvailabilityService(new AvailabilityRepository(store.ConnectionFactory)),
            new FakeSchedulerService(),
            new SchedulePlanTargetResolver(),
            new CsvExportService(),
            new DeviceImportExportService(new CsvExportService(), devices, maintenance),
            checkPolicy,
            settings,
            new FakeDialogService(),
            maintenance,
            new FakeWindowsServiceStatusService(),
            uiAutostartService: new FakeUiAutostartService(),
            deviceConnectionTestService: new DeviceConnectionTestService(new FakePingService(true)));
        await vm.InitializeAsync();
        return vm;
    }

    private static Task RunOnStaAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));

            _ = action().ContinueWith(task =>
            {
                if (task.Exception is not null)
                {
                    completion.SetException(task.Exception.InnerExceptions);
                }
                else if (task.IsCanceled)
                {
                    completion.SetCanceled();
                }
                else
                {
                    completion.SetResult();
                }

                dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            }, TaskScheduler.FromCurrentSynchronizationContext());

            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private sealed class ProbePage : PageViewModelBase
    {
        public ProbePage()
            : base("Probe", "Probe page")
        {
        }

        public bool FailOnLoad { get; set; }

        public bool NavigatedFromCalled { get; private set; }

        protected override Task LoadCoreAsync(CancellationToken cancellationToken)
        {
            if (FailOnLoad)
            {
                throw new InvalidOperationException("Probe failed.");
            }

            HasData = true;
            return Task.CompletedTask;
        }

        public override Task OnNavigatedFromAsync(CancellationToken cancellationToken)
        {
            NavigatedFromCalled = true;
            return base.OnNavigatedFromAsync(cancellationToken);
        }
    }

    private sealed class FakeWindowsServiceStatusService : IWindowsServiceStatusService
    {
        public Task<WindowsServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new WindowsServiceStatus("Stopped", "Durduruldu")
            {
                IsInstalled = true,
                StartupType = "3   DEMAND_START",
                IsManualStartup = true
            });
        }

        public Task<OperationResult> SetStartupTypeAsync(bool startWithWindows, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Ok());

        public Task<OperationResult> InstallAsync(CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Ok());

        public Task<OperationResult> UninstallAsync(CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Ok());

        public Task<OperationResult> StartAsync(CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Ok());

        public Task<OperationResult> StopAsync(CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Ok());

        public Task<OperationResult> RestartAsync(CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Ok());
    }

    private sealed class FakeDialogService : IDialogService
    {
        public void ShowInfo(string title, string message) { }

        public void ShowWarning(string title, string message) { }

        public void ShowError(string title, string message) { }

        public bool Confirm(string title, string message) => true;

        public CsvImportDuplicateAction ChooseDuplicateImportAction(string title, string message) => CsvImportDuplicateAction.SkipExisting;

        public void CopyToClipboard(string text) { }

        public string? GetOpenCsvFilePath() => null;

        public string? GetSaveCsvFilePath(string defaultFileName, string? initialDirectory = null) => null;

        public string? GetOpenDatabaseFilePath() => null;

        public string? GetSaveDatabaseFilePath(string defaultFileName) => null;

        public string? GetOpenJsonFilePath() => null;

        public string? GetSaveJsonFilePath(string defaultFileName) => null;
    }

    private sealed class FakeSchedulerService : ISchedulerService
    {
        public event EventHandler<SchedulerStatusChangedEventArgs>? StatusChanged;

        public bool IsRunning { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            IsRunning = true;
            StatusChanged?.Invoke(this, new SchedulerStatusChangedEventArgs("Baslatildi", shouldRefresh: false));
            return Task.CompletedTask;
        }

        public Task RunDuePlansOnceAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync()
        {
            IsRunning = false;
            StatusChanged?.Invoke(this, new SchedulerStatusChangedEventArgs("Durduruldu", shouldRefresh: false));
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeUiAutostartService : IUiAutostartService
    {
        public string ShortcutPath => string.Empty;

        public bool IsEnabled(string targetPath) => false;

        public Task SetEnabledAsync(bool enabled, string targetPath) => Task.CompletedTask;
    }
}
