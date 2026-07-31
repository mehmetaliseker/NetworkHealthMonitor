using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Models;
using NetworkHealthMonitor.Services;
using NetworkHealthMonitor.ViewModels;
using System.Windows.Threading;
using Xunit;

namespace NetworkHealthMonitor.Tests;

public sealed class ShellNavigationViewModelTests
{
    [Fact]
    public Task Navigation_commands_change_active_page_and_titles()
    {
        return RunOnStaAsync(async () =>
        {
            await using var store = await TestStore.CreateAsync();
            var vm = await CreateViewModelAsync(store);

            vm.NavigateLiveStatusCommand.Execute(null);
            Assert.True(vm.IsLiveStatusSection);
            Assert.Equal("Canlı Durum", vm.SectionTitle);

            vm.NavigateWorkerServiceCommand.Execute(null);
            Assert.True(vm.IsWorkerServiceSection);
            Assert.Equal("Worker Servisi", vm.SectionTitle);

            vm.NavigateSystemHealthCommand.Execute(null);
            Assert.True(vm.IsSystemHealthSection);
            Assert.Equal("Sistem Sağlığı", vm.SectionTitle);

            vm.NavigateReportsCommand.Execute(null);
            Assert.True(vm.IsReportsSection);
            Assert.Equal("Raporlar", vm.SectionTitle);

            await vm.DisposeAsync();
        });
    }

    [Fact]
    public Task Device_detail_navigation_populates_scoped_history()
    {
        return RunOnStaAsync(async () =>
        {
            await using var store = await TestStore.CreateAsync();
            var devices = new DeviceRepository(store.ConnectionFactory);
            var camera = CreateDevice("Depo Kamera 01", "192.0.2.50");
            camera.Id = await devices.AddAsync(camera);
            await new PingLogRepository(store.ConnectionFactory).AddAsync(new PingLog
            {
                DeviceId = camera.Id,
                DeviceName = camera.Name,
                IpAddress = camera.IpAddress,
                DeviceType = camera.DeviceType,
                Status = DeviceStatus.Online,
                IsReachable = true,
                LatencyMs = 12,
                CheckedAt = DateTime.Now,
                TriggerType = PingTriggerType.Manual
            });

            var vm = await CreateViewModelAsync(store);
            var loaded = Assert.Single(vm.Devices);

            vm.OpenDeviceDetailsCommand.Execute(loaded);

            Assert.True(vm.IsDeviceDetailsSection);
            Assert.True(vm.IsDevicesNavSelected);
            Assert.Equal("Depo Kamera 01", vm.SectionTitle);
            Assert.Single(vm.SelectedDevicePingLogs);

            await vm.DisposeAsync();
        });
    }

    [Fact]
    public Task Empty_loading_and_error_states_are_exposed()
    {
        return RunOnStaAsync(async () =>
        {
            await using var store = await TestStore.CreateAsync();
            var vm = await CreateViewModelAsync(store);

            vm.NavigateDevicesCommand.Execute(null);
            Assert.True(vm.HasNoDevices);
            Assert.Contains("cihaz", vm.PageEmptyText, StringComparison.OrdinalIgnoreCase);

            vm.IsBusy = true;
            Assert.Equal("Yükleniyor", vm.UiStatusText);
            Assert.Contains("yükleniyor", vm.PageLoadingText, StringComparison.OrdinalIgnoreCase);
            vm.IsBusy = false;

            vm.PageErrorMessage = "Cihazlar yüklenemedi.";
            Assert.True(vm.HasPageError);
            Assert.Equal("Cihazlar yüklenemedi.", vm.PageErrorText);
            vm.ClearPageErrorCommand.Execute(null);
            Assert.False(vm.HasPageError);

            await vm.DisposeAsync();
        });
    }

    [Fact]
    public Task Worker_status_and_settings_tabs_are_independent_from_filters()
    {
        return RunOnStaAsync(async () =>
        {
            await using var store = await TestStore.CreateAsync();
            var worker = new FakeWindowsServiceStatusService(new WindowsServiceStatus("Stopped", "Durduruldu")
            {
                IsInstalled = true,
                StartupType = "3   DEMAND_START",
                IsManualStartup = true
            });
            var vm = await CreateViewModelAsync(store, worker);

            vm.NavigateWorkerServiceCommand.Execute(null);
            Assert.Equal("Durduruldu", vm.WorkerHealthText);
            Assert.Equal("Manuel", vm.WorkerStartupBehaviorText);

            vm.DeviceSearchText = "kamera";
            vm.LogDeviceNameFilter = "switch";
            vm.LiveStatusFilter = DeviceStatus.Offline.ToDisplayName();

            vm.NavigateSettingsCommand.Execute(null);
            vm.NavigateSettingsPingRetryCommand.Execute(null);
            Assert.True(vm.IsSettingsPingRetrySection);
            Assert.Equal("kamera", vm.DeviceSearchText);
            Assert.Equal("switch", vm.LogDeviceNameFilter);
            Assert.Equal(DeviceStatus.Offline.ToDisplayName(), vm.LiveStatusFilter);

            vm.ClearLogFiltersCommand.Execute(null);
            Assert.Equal("kamera", vm.DeviceSearchText);
            Assert.Empty(vm.LogDeviceNameFilter);

            await vm.DisposeAsync();
        });
    }

    [Fact]
    public Task Navigation_does_not_reload_or_clear_loaded_collections()
    {
        return RunOnStaAsync(async () =>
        {
            await using var store = await TestStore.CreateAsync();
            var repository = new DeviceRepository(store.ConnectionFactory);
            var device = CreateDevice("Switch 01", "192.0.2.60", DeviceType.Switch);
            device.Id = await repository.AddAsync(device);
            var vm = await CreateViewModelAsync(store);

            var initialDeviceCount = vm.Devices.Count;
            var initialGroupCount = vm.DeviceGroups.Count;

            vm.NavigateLiveStatusCommand.Execute(null);
            vm.NavigateReportsCommand.Execute(null);
            vm.NavigateNotificationsCommand.Execute(null);

            Assert.Equal(initialDeviceCount, vm.Devices.Count);
            Assert.Equal(initialGroupCount, vm.DeviceGroups.Count);
            Assert.False(vm.IsBusy);

            await vm.DisposeAsync();
        });
    }

    private static async Task<MainViewModel> CreateViewModelAsync(
        TestStore store,
        IWindowsServiceStatusService? serviceStatusService = null)
    {
        await new AppSettingsService().SaveAsync(AppSettings.Default);
        var devices = new DeviceRepository(store.ConnectionFactory);
        var groups = new DeviceGroupRepository(store.ConnectionFactory);
        var logs = new PingLogRepository(store.ConnectionFactory);
        var schedules = new SchedulePlanRepository(store.ConnectionFactory);
        var outages = new OutageRepository(store.ConnectionFactory);
        var maintenance = new DataMaintenanceService(store.ConnectionFactory);
        var checkPolicy = new DeviceCheckPolicyService();
        var settings = new AppSettingsService();
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
            serviceStatusService ?? new FakeWindowsServiceStatusService(),
            uiAutostartService: new FakeUiAutostartService(),
            deviceConnectionTestService: new DeviceConnectionTestService(new FakePingService(true)));
        await vm.InitializeAsync();
        return vm;
    }

    private static Device CreateDevice(string name, string address, DeviceType type = DeviceType.Camera)
    {
        return new Device
        {
            Name = name,
            IpAddress = address,
            DeviceType = type,
            IsActive = true,
            IsEnabled = true,
            AutoCheckEnabled = true,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
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

    private sealed class FakeWindowsServiceStatusService : IWindowsServiceStatusService
    {
        private WindowsServiceStatus _status;

        public FakeWindowsServiceStatusService()
            : this(new WindowsServiceStatus("NotFound", "Kurulu değil") { IsInstalled = false })
        {
        }

        public FakeWindowsServiceStatusService(WindowsServiceStatus status)
        {
            _status = status;
        }

        public Task<WindowsServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_status);
        }

        public Task<OperationResult> SetStartupTypeAsync(bool startWithWindows, CancellationToken cancellationToken = default)
        {
            _status = _status with
            {
                StartupType = startWithWindows ? "2   AUTO_START" : "3   DEMAND_START",
                IsAutomaticStartup = startWithWindows,
                IsManualStartup = !startWithWindows
            };
            return Task.FromResult(OperationResult.Ok());
        }

        public Task<OperationResult> InstallAsync(CancellationToken cancellationToken = default)
        {
            _status = new WindowsServiceStatus("Stopped", "Durduruldu")
            {
                IsInstalled = true,
                StartupType = "3   DEMAND_START",
                IsManualStartup = true
            };
            return Task.FromResult(OperationResult.Ok());
        }

        public Task<OperationResult> UninstallAsync(CancellationToken cancellationToken = default)
        {
            _status = new WindowsServiceStatus("NotFound", "Kurulu değil") { IsInstalled = false };
            return Task.FromResult(OperationResult.Ok());
        }

        public Task<OperationResult> StartAsync(CancellationToken cancellationToken = default)
        {
            _status = _status with { Code = "Running", DisplayText = "Çalışıyor", IsRunning = true };
            return Task.FromResult(OperationResult.Ok());
        }

        public Task<OperationResult> StopAsync(CancellationToken cancellationToken = default)
        {
            _status = _status with { Code = "Stopped", DisplayText = "Durduruldu", IsRunning = false };
            return Task.FromResult(OperationResult.Ok());
        }

        public Task<OperationResult> RestartAsync(CancellationToken cancellationToken = default)
        {
            return StartAsync(cancellationToken);
        }
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
            StatusChanged?.Invoke(this, new SchedulerStatusChangedEventArgs("Başlatıldı", shouldRefresh: false));
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
