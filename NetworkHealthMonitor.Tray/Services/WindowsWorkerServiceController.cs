using System.ComponentModel;
using System.ServiceProcess;
using NetworkHealthMonitor.Services;

namespace NetworkHealthMonitor.Tray.Services;

public interface IWorkerServiceControlClient : IDisposable
{
    ServiceControllerStatus Status { get; }

    bool CanStop { get; }

    void Refresh();

    void Start();

    void Stop();

    void WaitForStatus(ServiceControllerStatus desiredStatus, TimeSpan timeout);
}

public interface IWorkerServiceControlClientFactory
{
    IWorkerServiceControlClient Create(string serviceName);
}

public sealed class WindowsWorkerServiceController : IWorkerServiceController
{
    private readonly string _serviceName;
    private readonly IWorkerServiceControlClientFactory _clientFactory;
    private readonly TimeSpan _startTimeout;
    private readonly TimeSpan _stopTimeout;

    public WindowsWorkerServiceController()
        : this(
            WorkerServiceConstants.ServiceName,
            new ServiceControllerClientFactory(),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30))
    {
    }

    public WindowsWorkerServiceController(
        string serviceName,
        IWorkerServiceControlClientFactory clientFactory,
        TimeSpan startTimeout,
        TimeSpan stopTimeout)
    {
        _serviceName = string.IsNullOrWhiteSpace(serviceName)
            ? WorkerServiceConstants.ServiceName
            : serviceName;
        _clientFactory = clientFactory;
        _startTimeout = startTimeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : startTimeout;
        _stopTimeout = stopTimeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : stopTimeout;
    }

    public Task<WorkerServiceStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            try
            {
                using var client = _clientFactory.Create(_serviceName);
                return ReadStatus(client);
            }
            catch (Exception ex) when (IsServiceNotFound(ex))
            {
                return WorkerServiceStatus.NotInstalled(ex.Message);
            }
            catch (Exception ex) when (IsAccessDenied(ex))
            {
                return WorkerServiceStatus.Error("Yetki gerekiyor", ex.Message);
            }
            catch (Exception ex)
            {
                return WorkerServiceStatus.Error("Hata", ex.Message);
            }
        }, cancellationToken);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return ExecuteAsync(client =>
        {
            var status = ReadStatus(client);
            if (status.State == WorkerServiceState.NotInstalled)
            {
                throw new WorkerServiceControlException(
                    WorkerServiceControlError.NotInstalled,
                    "Worker başlatılamadı. Windows hizmeti bulunamadı.");
            }

            if (status.State == WorkerServiceState.Running)
            {
                return;
            }

            if (status.State == WorkerServiceState.StartPending)
            {
                client.WaitForStatus(ServiceControllerStatus.Running, _startTimeout);
                return;
            }

            client.Start();
            client.WaitForStatus(ServiceControllerStatus.Running, _startTimeout);
        }, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return ExecuteAsync(client =>
        {
            var status = ReadStatus(client);
            if (status.State == WorkerServiceState.NotInstalled)
            {
                throw new WorkerServiceControlException(
                    WorkerServiceControlError.NotInstalled,
                    "Worker durdurulamadı. Windows hizmeti bulunamadı.");
            }

            if (status.State == WorkerServiceState.Stopped)
            {
                return;
            }

            if (status.State == WorkerServiceState.StopPending)
            {
                client.WaitForStatus(ServiceControllerStatus.Stopped, _stopTimeout);
                return;
            }

            if (!client.CanStop)
            {
                throw new WorkerServiceControlException(
                    WorkerServiceControlError.Failed,
                    "Worker durdurulamadı. Hizmet durdurma komutunu desteklemiyor.");
            }

            client.Stop();
            client.WaitForStatus(ServiceControllerStatus.Stopped, _stopTimeout);
        }, cancellationToken);
    }

    public Task RestartAsync(CancellationToken cancellationToken)
    {
        return ExecuteAsync(client =>
        {
            var status = ReadStatus(client);
            if (status.State == WorkerServiceState.NotInstalled)
            {
                throw new WorkerServiceControlException(
                    WorkerServiceControlError.NotInstalled,
                    "Worker yeniden başlatılamadı. Windows hizmeti bulunamadı.");
            }

            if (status.State is WorkerServiceState.Running or WorkerServiceState.StartPending)
            {
                if (!client.CanStop)
                {
                    throw new WorkerServiceControlException(
                        WorkerServiceControlError.Failed,
                        "Worker yeniden başlatılamadı. Hizmet durdurma komutunu desteklemiyor.");
                }

                client.Stop();
                client.WaitForStatus(ServiceControllerStatus.Stopped, _stopTimeout);
            }
            else if (status.State == WorkerServiceState.StopPending)
            {
                client.WaitForStatus(ServiceControllerStatus.Stopped, _stopTimeout);
            }

            client.Start();
            client.WaitForStatus(ServiceControllerStatus.Running, _startTimeout);
        }, cancellationToken);
    }

    public static WorkerServiceStatus MapStatus(ServiceControllerStatus status)
    {
        return status switch
        {
            ServiceControllerStatus.Running => new WorkerServiceStatus(WorkerServiceState.Running, "Çalışıyor"),
            ServiceControllerStatus.Stopped => new WorkerServiceStatus(WorkerServiceState.Stopped, "Durduruldu"),
            ServiceControllerStatus.StartPending => new WorkerServiceStatus(WorkerServiceState.StartPending, "Başlatılıyor"),
            ServiceControllerStatus.StopPending => new WorkerServiceStatus(WorkerServiceState.StopPending, "Durduruluyor"),
            ServiceControllerStatus.Paused => new WorkerServiceStatus(WorkerServiceState.Paused, "Duraklatıldı"),
            _ => new WorkerServiceStatus(WorkerServiceState.Unknown, "Bilinmiyor")
        };
    }

    private Task ExecuteAsync(Action<IWorkerServiceControlClient> action, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var client = _clientFactory.Create(_serviceName);
                action(client);
            }
            catch (Exception ex) when (ex is not WorkerServiceControlException)
            {
                throw WrapException(ex);
            }
        }, cancellationToken);
    }

    private static WorkerServiceStatus ReadStatus(IWorkerServiceControlClient client)
    {
        client.Refresh();
        return MapStatus(client.Status);
    }

    private static WorkerServiceControlException WrapException(Exception exception)
    {
        if (IsServiceNotFound(exception))
        {
            return new WorkerServiceControlException(
                WorkerServiceControlError.NotInstalled,
                "Worker işlemi tamamlanamadı. Windows hizmeti bulunamadı.",
                exception);
        }

        if (IsAccessDenied(exception))
        {
            return new WorkerServiceControlException(
                WorkerServiceControlError.AccessDenied,
                "Worker işlemi için yönetici yetkisi gerekiyor.",
                exception);
        }

        if (exception is System.TimeoutException)
        {
            return new WorkerServiceControlException(
                WorkerServiceControlError.Timeout,
                "Worker işlemi zaman aşımına uğradı.",
                exception);
        }

        return new WorkerServiceControlException(
            WorkerServiceControlError.Failed,
            "Worker işlemi tamamlanamadı.",
            exception);
    }

    private static bool IsAccessDenied(Exception exception)
    {
        return exception is UnauthorizedAccessException
               || exception is Win32Exception { NativeErrorCode: 5 }
               || exception.InnerException is not null && IsAccessDenied(exception.InnerException);
    }

    private static bool IsServiceNotFound(Exception exception)
    {
        return exception is Win32Exception { NativeErrorCode: 1060 }
               || exception.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
               || exception.Message.Contains("not exist", StringComparison.OrdinalIgnoreCase)
               || exception.Message.Contains("bulunamadı", StringComparison.OrdinalIgnoreCase)
               || exception.InnerException is not null && IsServiceNotFound(exception.InnerException);
    }
}

public sealed class ServiceControllerClientFactory : IWorkerServiceControlClientFactory
{
    public IWorkerServiceControlClient Create(string serviceName)
    {
        return new ServiceControllerClient(new ServiceController(serviceName));
    }
}

public sealed class ServiceControllerClient : IWorkerServiceControlClient
{
    private readonly ServiceController _serviceController;

    public ServiceControllerClient(ServiceController serviceController)
    {
        _serviceController = serviceController;
    }

    public ServiceControllerStatus Status => _serviceController.Status;

    public bool CanStop => _serviceController.CanStop;

    public void Refresh()
    {
        _serviceController.Refresh();
    }

    public void Start()
    {
        _serviceController.Start();
    }

    public void Stop()
    {
        _serviceController.Stop();
    }

    public void WaitForStatus(ServiceControllerStatus desiredStatus, TimeSpan timeout)
    {
        _serviceController.WaitForStatus(desiredStatus, timeout);
    }

    public void Dispose()
    {
        _serviceController.Dispose();
    }
}
