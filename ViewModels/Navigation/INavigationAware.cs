namespace NetworkHealthMonitor.ViewModels.Navigation;

public interface INavigationAware
{
    Task OnNavigatedToAsync(NavigationContext context, CancellationToken cancellationToken);

    Task OnNavigatedFromAsync(CancellationToken cancellationToken);
}
