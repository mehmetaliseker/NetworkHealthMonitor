namespace NetworkHealthMonitor.ViewModels.Navigation;

public sealed class NavigationService : INavigationService
{
    private const int MaxHistoryDepth = 24;
    private readonly IReadOnlyDictionary<Type, object> _pages;
    private readonly Stack<object> _backStack = new();
    private object? _currentPage;

    public NavigationService(IEnumerable<object> pages)
    {
        _pages = pages.ToDictionary(page => page.GetType());
    }

    public object? CurrentPage => _currentPage;

    public bool CanGoBack => _backStack.Count > 0;

    public event EventHandler? CurrentPageChanged;

    public async Task NavigateAsync<TViewModel>(
        object? parameter = null,
        CancellationToken cancellationToken = default)
        where TViewModel : class
    {
        if (!_pages.TryGetValue(typeof(TViewModel), out var nextPage))
        {
            throw new InvalidOperationException($"Navigation page is not registered: {typeof(TViewModel).Name}");
        }

        if (ReferenceEquals(_currentPage, nextPage) && parameter is null)
        {
            await NotifyNavigatedToAsync(nextPage, parameter, cancellationToken);
            return;
        }

        if (_currentPage is not null)
        {
            await NotifyNavigatedFromAsync(_currentPage, cancellationToken);
            _backStack.Push(_currentPage);
            TrimHistory();
        }

        _currentPage = nextPage;
        await NotifyNavigatedToAsync(nextPage, parameter, cancellationToken);
        CurrentPageChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task GoBackAsync(CancellationToken cancellationToken = default)
    {
        if (_backStack.Count == 0)
        {
            return;
        }

        if (_currentPage is not null)
        {
            await NotifyNavigatedFromAsync(_currentPage, cancellationToken);
        }

        _currentPage = _backStack.Pop();
        await NotifyNavigatedToAsync(_currentPage, null, cancellationToken);
        CurrentPageChanged?.Invoke(this, EventArgs.Empty);
    }

    private static async Task NotifyNavigatedToAsync(object page, object? parameter, CancellationToken cancellationToken)
    {
        if (page is INavigationAware aware)
        {
            await aware.OnNavigatedToAsync(new NavigationContext(page.GetType().Name, parameter), cancellationToken);
        }
    }

    private static async Task NotifyNavigatedFromAsync(object page, CancellationToken cancellationToken)
    {
        if (page is INavigationAware aware)
        {
            await aware.OnNavigatedFromAsync(cancellationToken);
        }
    }

    private void TrimHistory()
    {
        if (_backStack.Count <= MaxHistoryDepth)
        {
            return;
        }

        var retained = _backStack.Take(MaxHistoryDepth).Reverse().ToArray();
        _backStack.Clear();
        foreach (var item in retained)
        {
            _backStack.Push(item);
        }
    }
}
