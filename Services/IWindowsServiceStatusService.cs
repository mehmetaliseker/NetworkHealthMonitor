using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public interface IWindowsServiceStatusService
{
    Task<WindowsServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> SetStartupTypeAsync(bool startWithWindows, CancellationToken cancellationToken = default);

    Task<OperationResult> StartAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> StopAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> RestartAsync(CancellationToken cancellationToken = default);
}
