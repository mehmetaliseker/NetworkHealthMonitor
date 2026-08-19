namespace NetworkHealthMonitor.ViewModels.Navigation;

public interface IRefreshable
{
    Task RefreshAsync(CancellationToken cancellationToken);
}
