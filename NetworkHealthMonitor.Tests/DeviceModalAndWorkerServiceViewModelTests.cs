using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Infrastructure;
using NetworkHealthMonitor.Models;
using NetworkHealthMonitor.Services;
using NetworkHealthMonitor.ViewModels;
using Xunit;

namespace NetworkHealthMonitor.Tests;

public sealed class DeviceModalAndWorkerServiceViewModelTests
{
    [Fact]
    public Task New_device_command_opens_modal_and_validation_keeps_it_open()
    {
        return RunOnStaAsync(async () =>
        {
            await using var store = await TestStore.CreateAsync();
            var vm = await CreateViewModelAsync(store, new FakeWindowsServiceStatusService());

            vm.NavigateDeviceEditCommand.Execute(null);
            vm.FormName = "   ";
            vm.FormIpAddress = "http://127.0.0.1";

            vm.SaveDeviceCommand.Execute(null);
            await WaitForAsync(() => vm.HasDeviceNameValidationMessage && vm.HasDeviceAddressValidationMessage);

            Assert.True(vm.IsDeviceFormVisible);
            Assert.Contains("boş", vm.DeviceNameValidationMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("http://", vm.DeviceAddressValidationMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(await new DeviceRepository(store.ConnectionFactory).GetAllAsync());
        });
    }

    [Fact]
    public Task Device_modal_saves_to_sqlite_closes_and_duplicate_stays_visible()
    {
        return RunOnStaAsync(async () =>
        {
            await using var store = await TestStore.CreateAsync();
            var repository = new DeviceRepository(store.ConnectionFactory);
            var vm = await CreateViewModelAsync(store, new FakeWindowsServiceStatusService());

            vm.NavigateDeviceEditCommand.Execute(null);
            vm.FormName = "Geçici Modal Testi";
            vm.FormIpAddress = "127.0.0.1";
            vm.FormDeviceType = DeviceType.Camera;
            vm.FormPingTimeoutMs = 1000;

            vm.SaveDeviceCommand.Execute(null);
            await WaitForAsync(async () => (await repository.GetAllAsync()).Count == 1);

            var saved = Assert.Single(await repository.GetAllAsync());
            Assert.Equal("Geçici Modal Testi", saved.Name);
            Assert.Equal("127.0.0.1", saved.IpAddress);
            await WaitForAsync(() => !vm.IsDeviceFormVisible);
            await WaitForAsync(() => !vm.IsBusy);
            Assert.False(vm.IsDeviceFormVisible);

            vm.NavigateDeviceEditCommand.Execute(null);
            vm.FormName = "Duplicate";
            vm.FormIpAddress = "127.0.0.1";
            vm.SaveDeviceCommand.Execute(null);
            await WaitForAsync(() => vm.HasDeviceAddressValidationMessage);

            Assert.True(vm.IsDeviceFormVisible);
            Assert.Contains("kayıtlı", vm.DeviceAddressValidationMessage, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public Task Device_modal_connection_test_updates_status_without_saving()
    {
        return RunOnStaAsync(async () =>
        {
            await using var store = await TestStore.CreateAsync();
            var ping = new FakePingService(true);
            var vm = await CreateViewModelAsync(
                store,
                new FakeWindowsServiceStatusService(),
                new DeviceConnectionTestService(ping));

            vm.NavigateDeviceEditCommand.Execute(null);
            vm.FormName = "Ping Test";
            vm.FormIpAddress = "127.0.0.1";
            vm.FormPingTimeoutMs = 1000;

            vm.TestDeviceConnectionCommand.Execute(null);
            await WaitForAsync(() => !vm.IsTestingDeviceConnection && vm.HasDeviceConnectionTestMessage);

            Assert.Equal(1, ping.PingCount);
            Assert.Contains("başarılı", vm.DeviceConnectionStatusText, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(await new DeviceRepository(store.ConnectionFactory).GetAllAsync());
        });
    }

    [Fact]
    public Task Worker_startup_checkbox_uses_real_status_service_and_reverts_from_refresh()
    {
        return RunOnStaAsync(async () =>
        {
            await using var store = await TestStore.CreateAsync();
            var service = new FakeWindowsServiceStatusService(
                new WindowsServiceStatus("Stopped", "Durduruldu")
                {
                    StartupType = "3   DEMAND_START",
                    IsManualStartup = true,
                    IsInstalled = true
                });
            var vm = await CreateViewModelAsync(store, service);

            await vm.GetWorkerServiceStatusTextAsync();

            Assert.False(vm.WorkerAutostartEnabled);
            Assert.Equal("Manuel", vm.WorkerStartupBehaviorText);

            vm.WorkerAutostartEnabled = true;
            await WaitForAsync(() => service.SetStartupTypeCalls == 1 && !vm.IsWorkerServiceOperationInProgress);

            Assert.True(service.LastRequestedStartWithWindows);
            Assert.True(vm.WorkerAutostartEnabled);
            Assert.Equal("Otomatik", vm.WorkerStartupBehaviorText);
        });
    }

    private static async Task<MainViewModel> CreateViewModelAsync(
        TestStore store,
        IWindowsServiceStatusService serviceStatusService,
        IDeviceConnectionTestService? connectionTestService = null)
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

        return new MainViewModel(
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
            serviceStatusService,
            uiAutostartService: new FakeUiAutostartService(),
            deviceConnectionTestService: connectionTestService ?? new DeviceConnectionTestService(new FakePingService(true)));
    }

    private static Task RunOnStaAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(async () =>
        {
            try
            {
                await action();
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.True(condition());
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.True(await condition());
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

        public int SetStartupTypeCalls { get; private set; }

        public bool LastRequestedStartWithWindows { get; private set; }

        public Task<WindowsServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_status);
        }

        public Task<OperationResult> SetStartupTypeAsync(bool startWithWindows, CancellationToken cancellationToken = default)
        {
            SetStartupTypeCalls++;
            LastRequestedStartWithWindows = startWithWindows;
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
            _status = _status with
            {
                Code = "Stopped",
                DisplayText = "Durduruldu",
                IsInstalled = true,
                IsRunning = false,
                StartupType = "3   DEMAND_START",
                IsManualStartup = true,
                IsAutomaticStartup = false
            };
            return Task.FromResult(OperationResult.Ok("Worker servisi kuruldu."));
        }

        public Task<OperationResult> UninstallAsync(CancellationToken cancellationToken = default)
        {
            _status = new WindowsServiceStatus("NotFound", "Kurulu deÄŸil") { IsInstalled = false };
            return Task.FromResult(OperationResult.Ok("Worker servisi kaldÄ±rÄ±ldÄ±."));
        }

        public Task<OperationResult> StartAsync(CancellationToken cancellationToken = default)
        {
            _status = _status with { Code = "Running", DisplayText = "Çalışıyor", IsRunning = true };
            return Task.FromResult(OperationResult.Ok("Worker başlatıldı."));
        }

        public Task<OperationResult> StopAsync(CancellationToken cancellationToken = default)
        {
            _status = _status with { Code = "Stopped", DisplayText = "Durduruldu", IsRunning = false };
            return Task.FromResult(OperationResult.Ok("Worker durduruldu."));
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
