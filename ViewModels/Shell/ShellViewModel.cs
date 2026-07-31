using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using NetworkHealthMonitor.Infrastructure;
using NetworkHealthMonitor.Models;
using NetworkHealthMonitor.ViewModels.About;
using NetworkHealthMonitor.ViewModels.Dashboard;
using NetworkHealthMonitor.ViewModels.Devices;
using NetworkHealthMonitor.ViewModels.Help;
using NetworkHealthMonitor.ViewModels.Incidents;
using NetworkHealthMonitor.ViewModels.LiveStatus;
using NetworkHealthMonitor.ViewModels.Navigation;
using NetworkHealthMonitor.ViewModels.Notifications;
using NetworkHealthMonitor.ViewModels.Pages;
using NetworkHealthMonitor.ViewModels.PingHistory;
using NetworkHealthMonitor.ViewModels.Reports;
using NetworkHealthMonitor.ViewModels.Schedules;
using NetworkHealthMonitor.ViewModels.Settings;
using NetworkHealthMonitor.ViewModels.SystemHealth;
using NetworkHealthMonitor.ViewModels.Worker;

namespace NetworkHealthMonitor.ViewModels.Shell;

public sealed class ShellViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IReadOnlyList<object> _pages;
    private readonly NavigationService _navigationService;
    private bool _isDisposed;

    public ShellViewModel(MainViewModel legacy)
    {
        Legacy = legacy;

        DashboardPage = new DashboardViewModel(legacy, this);
        DevicesPage = new DevicesViewModel(legacy, this);
        DeviceDetailsPage = new DeviceDetailsViewModel(legacy, this);
        DeviceGroupsPage = new DeviceGroupsViewModel(legacy, this);
        LiveStatusPage = new LiveStatusViewModel(legacy, this);
        SchedulesPage = new SchedulesViewModel(legacy, this);
        PingHistoryPage = new PingHistoryViewModel(legacy, this);
        IncidentsPage = new IncidentsViewModel(legacy, this);
        NotificationsPage = new NotificationsViewModel(legacy, this);
        ReportsPage = new ReportsViewModel(legacy, this);
        WorkerServicePage = new WorkerServiceViewModel(legacy, this);
        SystemHealthPage = new SystemHealthViewModel(legacy, this);
        SettingsPage = new SettingsViewModel(legacy, this);
        HelpPage = new HelpViewModel(legacy, this);
        AboutPage = new AboutViewModel(legacy, this);

        _pages = new object[]
        {
            DashboardPage,
            DevicesPage,
            DeviceDetailsPage,
            DeviceGroupsPage,
            LiveStatusPage,
            SchedulesPage,
            PingHistoryPage,
            IncidentsPage,
            NotificationsPage,
            ReportsPage,
            WorkerServicePage,
            SystemHealthPage,
            SettingsPage,
            HelpPage,
            AboutPage
        };
        _navigationService = new NavigationService(_pages);
        _navigationService.CurrentPageChanged += NavigationServiceCurrentPageChanged;

        NavigateDashboardCommand = new AsyncRelayCommand(() => NavigateToAsync<DashboardViewModel>());
        NavigateDevicesCommand = new AsyncRelayCommand(() => NavigateToAsync<DevicesViewModel>());
        NavigateGroupsCommand = new AsyncRelayCommand(() => NavigateToAsync<DeviceGroupsViewModel>());
        NavigateLiveStatusCommand = new AsyncRelayCommand(() => NavigateToAsync<LiveStatusViewModel>());
        NavigateSchedulesCommand = new AsyncRelayCommand(() => NavigateToAsync<SchedulesViewModel>());
        NavigateLogsCommand = new AsyncRelayCommand(() => NavigateToAsync<PingHistoryViewModel>());
        NavigateEventsCommand = new AsyncRelayCommand(() => NavigateToAsync<IncidentsViewModel>());
        NavigateNotificationsCommand = new AsyncRelayCommand(() => NavigateToAsync<NotificationsViewModel>());
        NavigateReportsCommand = new AsyncRelayCommand(() => NavigateToAsync<ReportsViewModel>());
        NavigateWorkerServiceCommand = new AsyncRelayCommand(() => NavigateToAsync<WorkerServiceViewModel>());
        NavigateSystemHealthCommand = new AsyncRelayCommand(() => NavigateToAsync<SystemHealthViewModel>());
        NavigateSettingsCommand = new AsyncRelayCommand(() => NavigateToAsync<SettingsViewModel>());
        NavigateHelpCommand = new AsyncRelayCommand(() => NavigateToAsync<HelpViewModel>());
        NavigateAboutCommand = new AsyncRelayCommand(() => NavigateToAsync<AboutViewModel>());
        OpenDeviceDetailsCommand = new AsyncRelayCommand<Device>(device => NavigateToAsync<DeviceDetailsViewModel>(device), device => device is not null && !Legacy.IsBusy);
        GoBackCommand = new AsyncRelayCommand(() => _navigationService.GoBackAsync(), () => CanGoBack);
        RefreshCurrentPageCommand = new AsyncRelayCommand(RefreshCurrentPageAsync);
        ToggleNavigationCommand = new RelayCommand(() => IsNavigationCollapsed = !IsNavigationCollapsed);

        PrimaryNavigationItems = new ObservableCollection<NavigationItemViewModel>
        {
            CreateNavItem("01", "Genel Bakış", "Genel Bakış sayfasını aç", "Genel Bakış", NavigateDashboardCommand, () => IsDashboardNavSelected),
            CreateNavItem("02", "Cihazlar", "Cihazlar sayfasını aç", "Cihazlar", NavigateDevicesCommand, () => IsDevicesNavSelected),
            CreateNavItem("03", "Cihaz Grupları", "Cihaz Grupları sayfasını aç", "Cihaz Grupları", NavigateGroupsCommand, () => IsGroupsNavSelected),
            CreateNavItem("04", "Canlı Durum", "Canlı Durum sayfasını aç", "Canlı Durum", NavigateLiveStatusCommand, () => IsLiveStatusNavSelected),
            CreateNavItem("05", "Kontrol Planları", "Kontrol Planları sayfasını aç", "Kontrol Planları", NavigateSchedulesCommand, () => IsSchedulesNavSelected),
            CreateNavItem("06", "Ping Geçmişi", "Ping Geçmişi sayfasını aç", "Ping Geçmişi", NavigateLogsCommand, () => IsLogsNavSelected),
            CreateNavItem("07", "Kesintiler", "Kesintiler sayfasını aç", "Kesintiler", NavigateEventsCommand, () => IsEventsNavSelected),
            CreateNavItem("08", "Bildirimler", "Bildirimler sayfasını aç", "Bildirimler", NavigateNotificationsCommand, () => IsNotificationsNavSelected),
            CreateNavItem("09", "Raporlar", "Raporlar sayfasını aç", "Raporlar", NavigateReportsCommand, () => IsReportsNavSelected),
            CreateNavItem("10", WorkerServiceNavigationText, WorkerServiceNavigationAutomationName, WorkerServiceNavigationText, NavigateWorkerServiceCommand, () => IsWorkerServiceNavSelected),
            CreateNavItem("11", "Sistem Sağlığı", "Sistem Sağlığı sayfasını aç", "Sistem Sağlığı", NavigateSystemHealthCommand, () => IsSystemHealthNavSelected),
            CreateNavItem("12", "Ayarlar", "Ayarlar sayfasını aç", "Ayarlar", NavigateSettingsCommand, () => IsSettingsNavSelected)
        };

        SecondaryNavigationItems = new ObservableCollection<NavigationItemViewModel>
        {
            CreateNavItem("?", "Yardım", "Yardım sayfasını aç", "Yardım", NavigateHelpCommand, () => IsHelpNavSelected),
            CreateNavItem("i", "Hakkında", "Hakkında sayfasını aç", "Hakkında", NavigateAboutCommand, () => IsAboutNavSelected)
        };

        Legacy.PropertyChanged += LegacyPropertyChanged;
    }

    public MainViewModel Legacy { get; }

    public INavigationService NavigationService => _navigationService;

    public DashboardViewModel DashboardPage { get; }

    public DevicesViewModel DevicesPage { get; }

    public DeviceDetailsViewModel DeviceDetailsPage { get; }

    public DeviceGroupsViewModel DeviceGroupsPage { get; }

    public LiveStatusViewModel LiveStatusPage { get; }

    public SchedulesViewModel SchedulesPage { get; }

    public PingHistoryViewModel PingHistoryPage { get; }

    public IncidentsViewModel IncidentsPage { get; }

    public NotificationsViewModel NotificationsPage { get; }

    public ReportsViewModel ReportsPage { get; }

    public WorkerServiceViewModel WorkerServicePage { get; }

    public SystemHealthViewModel SystemHealthPage { get; }

    public SettingsViewModel SettingsPage { get; }

    public HelpViewModel HelpPage { get; }

    public AboutViewModel AboutPage { get; }

    public object? CurrentPage => _navigationService.CurrentPage;

    public bool CanGoBack => _navigationService.CanGoBack;

    public string PageTitle => CurrentPage is PageViewModelBase page ? page.Title : "Genel Bakış";

    public string PageDescription => CurrentPage is PageViewModelBase page ? page.Description : string.Empty;

    public string BreadcrumbText => CurrentPage is PageViewModelBase page ? page.BreadcrumbText : PageTitle;

    public string StatusMessage => Legacy.StatusMessage;

    public string UiStatusText => Legacy.UiStatusText;

    public string WorkerHealthText => Legacy.WorkerHealthText;

    public string WorkerServiceNavigationText => Legacy.WorkerServiceNavigationText;

    public string WorkerServiceNavigationAutomationName => Legacy.WorkerServiceNavigationAutomationName;

    public string NavigationToggleText => IsNavigationCollapsed ? "Menüyü genişlet" : "Menüyü daralt";

    public bool IsNavigationCollapsed
    {
        get => Legacy.IsNavigationCollapsed;
        set
        {
            if (Legacy.IsNavigationCollapsed == value)
            {
                return;
            }

            Legacy.IsNavigationCollapsed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NavigationToggleText));
        }
    }

    public ObservableCollection<NavigationItemViewModel> PrimaryNavigationItems { get; }

    public ObservableCollection<NavigationItemViewModel> SecondaryNavigationItems { get; }

    public ICommand NavigateDashboardCommand { get; }

    public ICommand NavigateDevicesCommand { get; }

    public ICommand NavigateGroupsCommand { get; }

    public ICommand NavigateLiveStatusCommand { get; }

    public ICommand NavigateSchedulesCommand { get; }

    public ICommand NavigateLogsCommand { get; }

    public ICommand NavigateEventsCommand { get; }

    public ICommand NavigateNotificationsCommand { get; }

    public ICommand NavigateReportsCommand { get; }

    public ICommand NavigateWorkerServiceCommand { get; }

    public ICommand NavigateSystemHealthCommand { get; }

    public ICommand NavigateSettingsCommand { get; }

    public ICommand NavigateHelpCommand { get; }

    public ICommand NavigateAboutCommand { get; }

    public ICommand OpenDeviceDetailsCommand { get; }

    public ICommand GoBackCommand { get; }

    public ICommand RefreshCurrentPageCommand { get; }

    public ICommand ToggleNavigationCommand { get; }

    public bool IsDashboardNavSelected => CurrentPage is DashboardViewModel;

    public bool IsDevicesNavSelected => CurrentPage is DevicesViewModel or DeviceDetailsViewModel;

    public bool IsGroupsNavSelected => CurrentPage is DeviceGroupsViewModel;

    public bool IsLiveStatusNavSelected => CurrentPage is LiveStatusViewModel;

    public bool IsSchedulesNavSelected => CurrentPage is SchedulesViewModel;

    public bool IsLogsNavSelected => CurrentPage is PingHistoryViewModel;

    public bool IsEventsNavSelected => CurrentPage is IncidentsViewModel;

    public bool IsNotificationsNavSelected => CurrentPage is NotificationsViewModel;

    public bool IsReportsNavSelected => CurrentPage is ReportsViewModel;

    public bool IsWorkerServiceNavSelected => CurrentPage is WorkerServiceViewModel;

    public bool IsSystemHealthNavSelected => CurrentPage is SystemHealthViewModel;

    public bool IsSettingsNavSelected => CurrentPage is SettingsViewModel;

    public bool IsHelpNavSelected => CurrentPage is HelpViewModel;

    public bool IsAboutNavSelected => CurrentPage is AboutViewModel;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return NavigateToAsync<DashboardViewModel>(cancellationToken: cancellationToken);
    }

    public async Task NavigateToAsync<TViewModel>(
        object? parameter = null,
        CancellationToken cancellationToken = default)
        where TViewModel : class
    {
        SyncLegacyNavigation(typeof(TViewModel), parameter);
        await _navigationService.NavigateAsync<TViewModel>(parameter, cancellationToken);
    }

    public async Task RefreshCurrentPageAsync()
    {
        if (CurrentPage is IRefreshable refreshable)
        {
            await refreshable.RefreshAsync(CancellationToken.None);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Legacy.PropertyChanged -= LegacyPropertyChanged;

        foreach (var page in _pages)
        {
            if (page is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();
            }
        }
    }

    private static NavigationItemViewModel CreateNavItem(
        string number,
        string label,
        string automationName,
        string toolTip,
        ICommand command,
        Func<bool> isSelectedResolver)
    {
        return new NavigationItemViewModel
        {
            Number = number,
            Label = label,
            AutomationName = automationName,
            ToolTip = toolTip,
            Command = command,
            IsSelectedResolver = isSelectedResolver
        };
    }

    private void SyncLegacyNavigation(Type pageType, object? parameter)
    {
        if (pageType == typeof(DashboardViewModel))
        {
            Legacy.NavigateDashboardCommand.Execute(null);
        }
        else if (pageType == typeof(DevicesViewModel))
        {
            Legacy.NavigateDevicesCommand.Execute(null);
        }
        else if (pageType == typeof(DeviceGroupsViewModel))
        {
            Legacy.NavigateGroupsCommand.Execute(null);
        }
        else if (pageType == typeof(LiveStatusViewModel))
        {
            Legacy.NavigateLiveStatusCommand.Execute(null);
        }
        else if (pageType == typeof(SchedulesViewModel))
        {
            Legacy.NavigateSchedulesCommand.Execute(null);
        }
        else if (pageType == typeof(PingHistoryViewModel))
        {
            Legacy.NavigateLogsCommand.Execute(null);
        }
        else if (pageType == typeof(IncidentsViewModel))
        {
            Legacy.NavigateEventsCommand.Execute(null);
        }
        else if (pageType == typeof(NotificationsViewModel))
        {
            Legacy.NavigateNotificationsCommand.Execute(null);
        }
        else if (pageType == typeof(ReportsViewModel))
        {
            Legacy.NavigateReportsCommand.Execute(null);
        }
        else if (pageType == typeof(WorkerServiceViewModel))
        {
            Legacy.NavigateWorkerServiceCommand.Execute(null);
        }
        else if (pageType == typeof(SystemHealthViewModel))
        {
            Legacy.NavigateSystemHealthCommand.Execute(null);
        }
        else if (pageType == typeof(SettingsViewModel))
        {
            Legacy.NavigateSettingsCommand.Execute(null);
        }
        else if (pageType == typeof(HelpViewModel))
        {
            Legacy.NavigateHelpCommand.Execute(null);
        }
        else if (pageType == typeof(AboutViewModel))
        {
            Legacy.NavigateAboutCommand.Execute(null);
        }
        else if (pageType == typeof(DeviceDetailsViewModel) && parameter is Device device)
        {
            Legacy.OpenDeviceDetailsCommand.Execute(device);
        }
    }

    private void NavigationServiceCurrentPageChanged(object? sender, EventArgs e)
    {
        NotifyNavigationState();
    }

    private void LegacyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.StatusMessage)
            or nameof(MainViewModel.WorkerHealthText)
            or nameof(MainViewModel.IsBusy)
            or nameof(MainViewModel.IsNavigationCollapsed))
        {
            OnPropertyChanged(nameof(StatusMessage));
            OnPropertyChanged(nameof(WorkerHealthText));
            OnPropertyChanged(nameof(UiStatusText));
            OnPropertyChanged(nameof(IsNavigationCollapsed));
            OnPropertyChanged(nameof(NavigationToggleText));
        }
    }

    private void NotifyNavigationState()
    {
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageDescription));
        OnPropertyChanged(nameof(BreadcrumbText));
        OnPropertyChanged(nameof(IsDashboardNavSelected));
        OnPropertyChanged(nameof(IsDevicesNavSelected));
        OnPropertyChanged(nameof(IsGroupsNavSelected));
        OnPropertyChanged(nameof(IsLiveStatusNavSelected));
        OnPropertyChanged(nameof(IsSchedulesNavSelected));
        OnPropertyChanged(nameof(IsLogsNavSelected));
        OnPropertyChanged(nameof(IsEventsNavSelected));
        OnPropertyChanged(nameof(IsNotificationsNavSelected));
        OnPropertyChanged(nameof(IsReportsNavSelected));
        OnPropertyChanged(nameof(IsWorkerServiceNavSelected));
        OnPropertyChanged(nameof(IsSystemHealthNavSelected));
        OnPropertyChanged(nameof(IsSettingsNavSelected));
        OnPropertyChanged(nameof(IsHelpNavSelected));
        OnPropertyChanged(nameof(IsAboutNavSelected));

        foreach (var item in PrimaryNavigationItems.Concat(SecondaryNavigationItems))
        {
            item.NotifySelectedChanged();
        }

        if (GoBackCommand is AsyncRelayCommand asyncRelayCommand)
        {
            asyncRelayCommand.NotifyCanExecuteChanged();
        }
    }
}
