using System.Windows.Input;
using NetworkHealthMonitor.Infrastructure;
using NetworkHealthMonitor.ViewModels.Navigation;

namespace NetworkHealthMonitor.ViewModels.Pages;

public abstract class PageViewModelBase : ObservableObject, INavigationAware, IRefreshable, IAsyncDisposable
{
    private CancellationTokenSource? _navigationCancellation;
    private bool _hasLoaded;
    private bool _isLoading;
    private bool _hasData;
    private string? _errorMessage;

    protected PageViewModelBase(string title, string description)
    {
        Title = title;
        Description = description;
        RetryCommand = new AsyncRelayCommand(() => RefreshAsync(CancellationToken.None));
        RefreshCommand = new AsyncRelayCommand(() => RefreshAsync(CancellationToken.None));
    }

    public string Title { get; }

    public string Description { get; }

    public bool IsLoading
    {
        get => _isLoading;
        protected set => SetProperty(ref _isLoading, value);
    }

    public bool HasData
    {
        get => _hasData;
        protected set => SetProperty(ref _hasData, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        protected set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public ICommand RetryCommand { get; }

    public ICommand RefreshCommand { get; }

    public int LoadCount { get; private set; }

    public virtual string BreadcrumbText => Title;

    public async Task OnNavigatedToAsync(NavigationContext context, CancellationToken cancellationToken)
    {
        _navigationCancellation?.Cancel();
        _navigationCancellation?.Dispose();
        _navigationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        await OnBeforeNavigatedToAsync(context, _navigationCancellation.Token);
        if (!_hasLoaded || ShouldReloadOnNavigate(context))
        {
            await LoadAsync(_navigationCancellation.Token);
        }
        else
        {
            UpdateStateFromCachedData();
        }
    }

    public virtual Task OnNavigatedFromAsync(CancellationToken cancellationToken)
    {
        _navigationCancellation?.Cancel();
        return Task.CompletedTask;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;
            LoadCount++;
            await LoadCoreAsync(cancellationToken);
            _hasLoaded = true;
            UpdateStateFromCachedData();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ErrorMessage = BuildUserErrorMessage(ex);
            HasData = false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public virtual ValueTask DisposeAsync()
    {
        _navigationCancellation?.Cancel();
        _navigationCancellation?.Dispose();
        return ValueTask.CompletedTask;
    }

    protected virtual Task OnBeforeNavigatedToAsync(NavigationContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    protected virtual bool ShouldReloadOnNavigate(NavigationContext context)
    {
        return false;
    }

    protected abstract Task LoadCoreAsync(CancellationToken cancellationToken);

    protected virtual bool ResolveHasData()
    {
        return true;
    }

    protected void UpdateStateFromCachedData()
    {
        HasData = ResolveHasData();
    }

    private static string BuildUserErrorMessage(Exception exception)
    {
        return exception is InvalidOperationException
            ? exception.Message
            : "Icerik yuklenemedi. Ayrintilar uygulama loglarina yazildi.";
    }
}
