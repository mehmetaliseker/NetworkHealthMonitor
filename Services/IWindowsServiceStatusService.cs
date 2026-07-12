namespace NetworkHealthMonitor.Services;

public interface IWindowsServiceStatusService
{
    Task<WindowsServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}
