using System.ServiceProcess;
using NetworkHealthMonitor.Services;
using NetworkHealthMonitor.Tray.Services;
using Xunit;

namespace NetworkHealthMonitor.Tests;

public sealed class TrayServiceControllerTests
{
    [Fact]
    public async Task Service_not_found_returns_not_installed_status()
    {
        var controller = CreateController(new ThrowingFactory(new InvalidOperationException("Service does not exist.")));

        var status = await controller.GetStatusAsync(CancellationToken.None);

        Assert.Equal(WorkerServiceState.NotInstalled, status.State);
        Assert.False(status.IsInstalled);
    }

    [Fact]
    public void Running_status_is_mapped()
    {
        var status = WindowsWorkerServiceController.MapStatus(ServiceControllerStatus.Running);

        Assert.Equal(WorkerServiceState.Running, status.State);
        Assert.True(status.IsRunning);
    }

    [Fact]
    public void Stopped_status_is_mapped()
    {
        var status = WindowsWorkerServiceController.MapStatus(ServiceControllerStatus.Stopped);

        Assert.Equal(WorkerServiceState.Stopped, status.State);
        Assert.True(status.IsStopped);
    }

    [Fact]
    public async Task Start_command_starts_stopped_service()
    {
        var client = new FakeClient(ServiceControllerStatus.Stopped);
        var controller = CreateController(new FakeFactory(client));

        await controller.StartAsync(CancellationToken.None);

        Assert.Equal(1, client.StartCount);
        Assert.Contains(ServiceControllerStatus.Running, client.WaitedStatuses);
    }

    [Fact]
    public async Task Stop_command_stops_running_service()
    {
        var client = new FakeClient(ServiceControllerStatus.Running);
        var controller = CreateController(new FakeFactory(client));

        await controller.StopAsync(CancellationToken.None);

        Assert.Equal(1, client.StopCount);
        Assert.Contains(ServiceControllerStatus.Stopped, client.WaitedStatuses);
    }

    [Fact]
    public async Task Restart_command_stops_then_starts_running_service()
    {
        var client = new FakeClient(ServiceControllerStatus.Running);
        var controller = CreateController(new FakeFactory(client));

        await controller.RestartAsync(CancellationToken.None);

        Assert.Equal(1, client.StopCount);
        Assert.Equal(1, client.StartCount);
        Assert.Equal(new[] { ServiceControllerStatus.Stopped, ServiceControllerStatus.Running }, client.WaitedStatuses);
    }

    [Fact]
    public async Task Timeout_is_reported_as_control_exception()
    {
        var client = new FakeClient(ServiceControllerStatus.Stopped)
        {
            TimeoutOnWait = true
        };
        var controller = CreateController(new FakeFactory(client));

        var ex = await Assert.ThrowsAsync<WorkerServiceControlException>(() => controller.StartAsync(CancellationToken.None));

        Assert.Equal(WorkerServiceControlError.Timeout, ex.Error);
    }

    [Fact]
    public async Task Access_denied_is_reported_as_control_exception()
    {
        var client = new FakeClient(ServiceControllerStatus.Stopped)
        {
            StartException = new UnauthorizedAccessException("Access denied.")
        };
        var controller = CreateController(new FakeFactory(client));

        var ex = await Assert.ThrowsAsync<WorkerServiceControlException>(() => controller.StartAsync(CancellationToken.None));

        Assert.Equal(WorkerServiceControlError.AccessDenied, ex.Error);
    }

    [Fact]
    public void Single_instance_guard_allows_only_one_owner()
    {
        var name = @"Local\NetworkHealthMonitor.Tests." + Guid.NewGuid().ToString("N");
        using var first = new SingleInstanceGuard(name);
        using var second = new SingleInstanceGuard(name);

        Assert.True(first.IsFirstInstance);
        Assert.False(second.IsFirstInstance);
    }

    [Fact]
    public void Menu_state_disables_start_when_running()
    {
        var menu = TrayMenuStateBuilder.Build(new WorkerServiceStatus(WorkerServiceState.Running, "Çalışıyor"));

        Assert.False(menu.CanStart);
        Assert.True(menu.CanStop);
        Assert.True(menu.CanRestart);
        Assert.False(menu.ShowInstall);
    }

    [Fact]
    public void Menu_state_disables_stop_and_restart_when_stopped()
    {
        var menu = TrayMenuStateBuilder.Build(new WorkerServiceStatus(WorkerServiceState.Stopped, "Durduruldu"));

        Assert.True(menu.CanStart);
        Assert.False(menu.CanStop);
        Assert.False(menu.CanRestart);
    }

    [Fact]
    public void Menu_state_shows_install_when_service_is_missing()
    {
        var menu = TrayMenuStateBuilder.Build(WorkerServiceStatus.NotInstalled());

        Assert.True(menu.ShowInstall);
        Assert.False(menu.CanStart);
        Assert.False(menu.CanStop);
        Assert.False(menu.CanRestart);
    }

    private static WindowsWorkerServiceController CreateController(IWorkerServiceControlClientFactory factory)
    {
        return new WindowsWorkerServiceController(
            WorkerServiceConstants.ServiceName,
            factory,
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(10));
    }

    private sealed class FakeFactory : IWorkerServiceControlClientFactory
    {
        private readonly IWorkerServiceControlClient _client;

        public FakeFactory(IWorkerServiceControlClient client)
        {
            _client = client;
        }

        public IWorkerServiceControlClient Create(string serviceName)
        {
            return _client;
        }
    }

    private sealed class ThrowingFactory : IWorkerServiceControlClientFactory
    {
        private readonly Exception _exception;

        public ThrowingFactory(Exception exception)
        {
            _exception = exception;
        }

        public IWorkerServiceControlClient Create(string serviceName)
        {
            throw _exception;
        }
    }

    private sealed class FakeClient : IWorkerServiceControlClient
    {
        public FakeClient(ServiceControllerStatus status)
        {
            Status = status;
        }

        public ServiceControllerStatus Status { get; private set; }

        public bool CanStop { get; set; } = true;

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public bool TimeoutOnWait { get; set; }

        public Exception? StartException { get; set; }

        public List<ServiceControllerStatus> WaitedStatuses { get; } = [];

        public void Refresh()
        {
        }

        public void Start()
        {
            StartCount++;
            if (StartException is not null)
            {
                throw StartException;
            }
        }

        public void Stop()
        {
            StopCount++;
        }

        public void WaitForStatus(ServiceControllerStatus desiredStatus, TimeSpan timeout)
        {
            WaitedStatuses.Add(desiredStatus);
            if (TimeoutOnWait)
            {
                throw new System.TimeoutException("Timed out.");
            }

            Status = desiredStatus;
        }

        public void Dispose()
        {
        }
    }
}
