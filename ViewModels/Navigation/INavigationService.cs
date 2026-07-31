namespace NetworkHealthMonitor.ViewModels.Navigation;

public interface INavigationService
{
    object? CurrentPage { get; }

    bool CanGoBack { get; }

    event EventHandler? CurrentPageChanged;

    Task NavigateAsync<TViewModel>(
        object? parameter = null,
        CancellationToken cancellationToken = default)
        where TViewModel : class;

    Task GoBackAsync(CancellationToken cancellationToken = default);
}
