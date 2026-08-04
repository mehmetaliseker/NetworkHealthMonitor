using System.Windows;
using System.Windows.Controls;
using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Models;
using NetworkHealthMonitor.Services;
using NetworkHealthMonitor.ViewModels;
using NetworkHealthMonitor.ViewModels.Shell;
using Xunit;

namespace NetworkHealthMonitor.Tests;

[Collection(SequentialDatabaseCollection.Name)]
public sealed class DeviceUiFixTests
{
    [Fact]
    public Task Device_crud_edit_and_delete_persist_in_sqlite()
    {
        return RunOnStaAsync(async () =>
        {
            await using var store = await TestStore.CreateAsync();
            var repository = new DeviceRepository(store.ConnectionFactory);
            var dialogs = new RecordingDialogService();
            var vm = await CreateViewModelAsync(store, dialogs);

            vm.NavigateDeviceEditCommand.Execute(null);
            Assert.True(vm.IsAnyDialogVisible);
            vm.FormName = "Smoke Kamera";
            vm.FormIpAddress = "192.0.2.10";
            vm.FormDeviceType = DeviceType.Camera;
            vm.SaveDeviceCommand.Execute(null);
            await WaitForAsync(() => !vm.IsBusy && vm.Devices.Count == 1 && !vm.IsDeviceFormVisible);

            var created = Assert.Single(await repository.GetAllAsync());
            Assert.Equal("Smoke Kamera", created.Name);

            vm.EditDeviceCommand.Execute(Assert.Single(vm.Devices));
            Assert.True(vm.IsDeviceFormVisible);
            Assert.True(vm.IsAnyDialogVisible);
            vm.FormName = "Smoke Kamera Güncel";
            vm.FormIpAddress = "192.0.2.11";
            vm.SaveDeviceCommand.Execute(null);
            await WaitForAsync(() =>
                !vm.IsBusy
                && !vm.IsDeviceFormVisible
                && vm.Devices.Any(device => device.Name == "Smoke Kamera Güncel" && device.IpAddress == "192.0.2.11"));

            var updated = Assert.Single(await repository.GetAllAsync());
            Assert.Equal("Smoke Kamera Güncel", updated.Name);
            Assert.Equal("192.0.2.11", updated.IpAddress);

            dialogs.ConfirmResult = true;
            vm.DeleteDeviceCommand.Execute(vm.Devices.Single());
            await WaitForAsync(async () => (await repository.GetAllAsync()).Count == 0);

            Assert.Empty(await repository.GetAllAsync());
            await vm.DisposeAsync();
        });
    }

    [Fact]
    public Task Bulk_activate_shows_selected_count_and_result_summary()
    {
        return RunOnStaAsync(async () =>
        {
            await using var store = await TestStore.CreateAsync();
            var repository = new DeviceRepository(store.ConnectionFactory);
            var first = CreateInactiveDevice("Bulk A", "192.0.2.21");
            var second = CreateInactiveDevice("Bulk B", "192.0.2.22");
            first.Id = await repository.AddAsync(first);
            second.Id = await repository.AddAsync(second);

            var dialogs = new RecordingDialogService { ConfirmResult = true };
            var vm = await CreateViewModelAsync(store, dialogs);
            Assert.Equal(2, vm.Devices.Count);
            foreach (var device in vm.Devices)
            {
                device.IsSelected = true;
            }

            vm.ActivateSelectedDevicesCommand.Execute(null);
            await WaitForAsync(() => dialogs.InfoMessages.Count > 0 && !vm.IsBusy);

            Assert.Contains(dialogs.InfoMessages, message => message.Contains("2 cihaz seçildi", StringComparison.Ordinal));
            Assert.Contains(dialogs.InfoMessages, message => message.Contains("aktif", StringComparison.OrdinalIgnoreCase));
            var devices = await repository.GetAllAsync();
            Assert.All(devices, device => Assert.True(device.IsActive));
            await vm.DisposeAsync();
        });
    }

    [Fact]
    public Task Bulk_delete_shows_selected_count_and_result_summary()
    {
        return RunOnStaAsync(async () =>
        {
            await using var store = await TestStore.CreateAsync();
            var repository = new DeviceRepository(store.ConnectionFactory);
            var first = CreateInactiveDevice("Del A", "192.0.2.31");
            first.IsActive = true;
            first.IsEnabled = true;
            first.Id = await repository.AddAsync(first);
            var second = CreateInactiveDevice("Del B", "192.0.2.32");
            second.IsActive = true;
            second.IsEnabled = true;
            second.Id = await repository.AddAsync(second);

            var dialogs = new RecordingDialogService { ConfirmResult = true };
            var vm = await CreateViewModelAsync(store, dialogs);
            foreach (var device in vm.Devices)
            {
                device.IsSelected = true;
            }

            vm.DeleteSelectedDevicesBulkCommand.Execute(null);
            await WaitForAsync(() => dialogs.InfoMessages.Count > 0 && !vm.IsBusy);

            Assert.Contains(dialogs.InfoTitles, title => title.Contains("silme", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(dialogs.InfoMessages, message => message.Contains("2 cihaz seçildi", StringComparison.Ordinal));
            Assert.Empty(await repository.GetAllAsync());
            await vm.DisposeAsync();
        });
    }

    [Fact]
    public Task Navigation_collapsed_state_persists_in_settings()
    {
        return RunOnStaAsync(async () =>
        {
            await using var store = await TestStore.CreateAsync();
            var settingsService = new AppSettingsService();
            await settingsService.SaveAsync(AppSettings.Default);

            var vm = await CreateViewModelAsync(store, new RecordingDialogService(), settingsService);
            Assert.False(vm.IsNavigationCollapsed);

            vm.IsNavigationCollapsed = true;
            await WaitForAsync(async () => (await settingsService.LoadAsync()).IsNavigationCollapsed);

            var persisted = await settingsService.LoadAsync();
            Assert.True(persisted.IsNavigationCollapsed);

            var reloaded = await CreateViewModelAsync(store, new RecordingDialogService(), settingsService);
            Assert.True(reloaded.IsNavigationCollapsed);
            await vm.DisposeAsync();
            await reloaded.DisposeAsync();
        });
    }

    [Fact]
    public Task Shell_navigation_items_use_icon_glyphs_instead_of_numbers()
    {
        return RunOnStaAsync(async () =>
        {
            await using var store = await TestStore.CreateAsync();
            var legacy = await CreateViewModelAsync(store, new RecordingDialogService());
            var shell = new ShellViewModel(legacy);

            Assert.All(shell.PrimaryNavigationItems, item =>
            {
                Assert.False(string.IsNullOrWhiteSpace(item.IconGlyph));
                Assert.DoesNotMatch("^\\d{2}$", item.IconGlyph);
            });

            Assert.Equal("\uE772", shell.PrimaryNavigationItems.ElementAt(1).IconGlyph);
            Assert.Equal("Cihazlar", shell.PrimaryNavigationItems.ElementAt(1).Label);
            await shell.DisposeAsync();
            await legacy.DisposeAsync();
        });
    }

    [Fact]
    public Task Device_dialog_host_hit_test_visibility_tracks_dialog_state()
    {
        return RunOnStaAsync(async () =>
        {
            await using var store = await TestStore.CreateAsync();
            var vm = await CreateViewModelAsync(store, new RecordingDialogService());

            Assert.False(vm.IsAnyDialogVisible);

            vm.NavigateDeviceEditCommand.Execute(null);
            Assert.True(vm.IsDeviceFormVisible);
            Assert.True(vm.IsAnyDialogVisible);

            vm.CloseDeviceFormCommand.Execute(null);
            Assert.False(vm.IsDeviceFormVisible);
            Assert.False(vm.IsAnyDialogVisible);

            // Regression guard: parent IsHitTestVisible=False blocks children even when child is True.
            var blockedParent = new Grid { IsHitTestVisible = false, Width = 200, Height = 200 };
            var child = new Button
            {
                IsHitTestVisible = true,
                Content = "Kaydet",
                Width = 80,
                Height = 32,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            blockedParent.Children.Add(child);

            var window = new Window
            {
                Content = blockedParent,
                Width = 240,
                Height = 240,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
                Left = -20000,
                Top = -20000
            };
            window.Show();
            window.UpdateLayout();

            var relative = new System.Windows.Point(100, 100);
            Assert.Null(blockedParent.InputHitTest(relative));

            blockedParent.IsHitTestVisible = true;
            Assert.NotNull(blockedParent.InputHitTest(relative));

            window.Close();
            await vm.DisposeAsync();
        });
    }

    private static Device CreateInactiveDevice(string name, string ip)
    {
        return new Device
        {
            Name = name,
            IpAddress = ip,
            DeviceType = DeviceType.Camera,
            IsActive = false,
            IsEnabled = false,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
    }

    private static async Task<MainViewModel> CreateViewModelAsync(
        TestStore store,
        IDialogService dialogService,
        AppSettingsService? settingsService = null)
    {
        settingsService ??= new AppSettingsService();

        var devices = new DeviceRepository(store.ConnectionFactory);
        var groups = new DeviceGroupRepository(store.ConnectionFactory);
        var logs = new PingLogRepository(store.ConnectionFactory);
        var schedules = new SchedulePlanRepository(store.ConnectionFactory);
        var outages = new OutageRepository(store.ConnectionFactory);
        var checkPolicy = new DeviceCheckPolicyService();
        var maintenance = new DataMaintenanceService(store.ConnectionFactory);
        var pingExecution = new PingExecutionService(
            devices,
            groups,
            logs,
            outages,
            new FakePingService(true),
            checkPolicy,
            new DeviceHealthEvaluator(),
            settingsService);

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
            settingsService,
            dialogService,
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
            var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(
                new System.Windows.Threading.DispatcherSynchronizationContext(dispatcher));

            Task work;
            try
            {
                work = action();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
                dispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Background);
                System.Windows.Threading.Dispatcher.Run();
                return;
            }

            _ = work.ContinueWith(task =>
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

                dispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Background);
            }, TaskScheduler.FromCurrentSynchronizationContext());

            System.Windows.Threading.Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 120; attempt++)
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
        for (var attempt = 0; attempt < 120; attempt++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.True(await condition());
    }

    private sealed class RecordingDialogService : IDialogService
    {
        public bool ConfirmResult { get; set; } = true;

        public List<string> InfoTitles { get; } = new();

        public List<string> InfoMessages { get; } = new();

        public void ShowInfo(string title, string message)
        {
            InfoTitles.Add(title);
            InfoMessages.Add(message);
        }

        public void ShowWarning(string title, string message)
        {
        }

        public void ShowError(string title, string message)
        {
        }

        public bool Confirm(string title, string message) => ConfirmResult;

        public CsvImportDuplicateAction ChooseDuplicateImportAction(string title, string message)
            => CsvImportDuplicateAction.Cancel;

        public void CopyToClipboard(string text)
        {
        }

        public string? GetOpenCsvFilePath() => null;

        public string? GetSaveCsvFilePath(string defaultFileName, string? initialDirectory = null) => null;

        public string? GetOpenDatabaseFilePath() => null;

        public string? GetSaveDatabaseFilePath(string defaultFileName) => null;

        public string? GetOpenJsonFilePath() => null;

        public string? GetSaveJsonFilePath(string defaultFileName) => null;
    }

    private sealed class FakeWindowsServiceStatusService : IWindowsServiceStatusService
    {
        public Task<WindowsServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new WindowsServiceStatus("NotFound", "Kurulu değil") { IsInstalled = false });

        public Task<OperationResult> SetStartupTypeAsync(bool startWithWindows, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Ok("ok"));

        public Task<OperationResult> InstallAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Ok("ok"));

        public Task<OperationResult> UninstallAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Ok("ok"));

        public Task<OperationResult> StartAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Ok("ok"));

        public Task<OperationResult> StopAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Ok("ok"));

        public Task<OperationResult> RestartAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Ok("ok"));
    }

    private sealed class FakeUiAutostartService : IUiAutostartService
    {
        public string ShortcutPath => string.Empty;

        public bool IsEnabled(string targetPath) => false;

        public Task SetEnabledAsync(bool enabled, string targetPath) => Task.CompletedTask;
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
}
