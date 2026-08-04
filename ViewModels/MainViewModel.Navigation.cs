using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Infrastructure;
using NetworkHealthMonitor.Models;
using NetworkHealthMonitor.Services;

namespace NetworkHealthMonitor.ViewModels;

public sealed partial class MainViewModel
{
    private const string SectionDeviceDetails = "Cihaz Detayı";
    private const string SectionLiveStatus = "Canlı Durum";
    private const string SectionWorkerService = "Worker Servisi";
    private const string SectionSystemHealth = "Sistem Sağlığı";
    private const string SectionHelp = "Yardım";
    private const string SectionAbout = "Hakkında";

    private RelayCommand? _navigateLiveStatusCommand;
    private RelayCommand? _navigateWorkerServiceCommand;
    private RelayCommand? _navigateSystemHealthCommand;
    private RelayCommand? _navigateHelpCommand;
    private RelayCommand? _navigateAboutCommand;
    private RelayCommand? _openSelectedDeviceDetailsCommand;
    private RelayCommand<Device>? _openDeviceDetailsCommand;
    private RelayCommand<DeviceGroupSummaryViewModel>? _openGroupDetailsCommand;
    private AsyncRelayCommand<DeviceGroupSummaryViewModel>? _pingGroupSummaryCommand;
    private RelayCommand? _openSchedulePlanFormCommand;
    private RelayCommand? _closeSchedulePlanFormCommand;
    private RelayCommand<string>? _selectReportCommand;
    private RelayCommand? _clearPageErrorCommand;
    private RelayCommand? _clearLogFiltersCommand;
    private AsyncRelayCommand? _installWorkerServiceCommand;
    private AsyncRelayCommand? _uninstallWorkerServiceCommand;
    private PingLog? _selectedLog;
    private Outage? _selectedOutage;
    private string _pageErrorMessage = string.Empty;
    private string _liveGroupFilter = AllGroupsText;
    private string _liveTypeFilter = AllDeviceTypesText;
    private string _liveStatusFilter = AllStatusesText;
    private bool _isSchedulePlanFormVisible;
    private string _selectedReportName = "Cihaz Uptime Raporu";
    private DateTime? _reportStartDate = DateTime.Today.AddDays(-30);
    private DateTime? _reportEndDate = DateTime.Today;
    private int? _reportDeviceFilterId;
    private int? _reportGroupFilterId;

    public ObservableCollection<SummaryCardViewModel> OverviewSummaryCards { get; } = new();

    public ObservableCollection<DashboardAlertViewModel> RecentAlerts { get; } = new();

    public ObservableCollection<DeviceGroupSummaryViewModel> DeviceGroupSummaries { get; } = new();

    public ObservableCollection<Device> SelectedGroupDevices { get; } = new();

    public ObservableCollection<Device> LiveOnlineDevices { get; } = new();

    public ObservableCollection<Device> LiveOfflineDevices { get; } = new();

    public ObservableCollection<Device> LiveCheckedDevices { get; } = new();

    public ObservableCollection<PingLog> LiveRecentChanges { get; } = new();

    public ObservableCollection<PingLog> SelectedDevicePingLogs { get; } = new();

    public ObservableCollection<Outage> SelectedDeviceOutages { get; } = new();

    public ObservableCollection<NotificationOutboxItem> SelectedDeviceNotifications { get; } = new();

    public ObservableCollection<Outage> RecentOutages { get; } = new();

    public ObservableCollection<Outage> ResolvedOutages { get; } = new();

    public ObservableCollection<string> ReportOptions { get; } = new(new[]
    {
        "Cihaz Uptime Raporu",
        "Kesinti Süreleri",
        "SLA Raporu",
        "MTTR / MTBF",
        "Ping Performansı",
        "Bildirim Başarı Oranı"
    });

    public RelayCommand NavigateLiveStatusCommand => _navigateLiveStatusCommand ??= new RelayCommand(() => NavigateToSection(SectionLiveStatus));

    public RelayCommand NavigateWorkerServiceCommand => _navigateWorkerServiceCommand ??= new RelayCommand(() => NavigateToSection(SectionWorkerService));

    public RelayCommand NavigateSystemHealthCommand => _navigateSystemHealthCommand ??= new RelayCommand(() => NavigateToSection(SectionSystemHealth));

    public RelayCommand NavigateHelpCommand => _navigateHelpCommand ??= new RelayCommand(() => NavigateToSection(SectionHelp));

    public RelayCommand NavigateAboutCommand => _navigateAboutCommand ??= new RelayCommand(() => NavigateToSection(SectionAbout));

    public RelayCommand OpenSelectedDeviceDetailsCommand => _openSelectedDeviceDetailsCommand ??= new RelayCommand(
        () => OpenDeviceDetails(SelectedDevice),
        () => SelectedDevice is not null && !IsBusy);

    public RelayCommand<Device> OpenDeviceDetailsCommand => _openDeviceDetailsCommand ??= new RelayCommand<Device>(
        OpenDeviceDetails,
        device => device is not null && !IsBusy);

    public RelayCommand<DeviceGroupSummaryViewModel> OpenGroupDetailsCommand => _openGroupDetailsCommand ??= new RelayCommand<DeviceGroupSummaryViewModel>(
        OpenGroupDetails,
        summary => summary?.Group is not null);

    public AsyncRelayCommand<DeviceGroupSummaryViewModel> PingGroupSummaryCommand => _pingGroupSummaryCommand ??= new AsyncRelayCommand<DeviceGroupSummaryViewModel>(
        summary => summary?.Group is null ? Task.CompletedTask : PingGroupFromSummaryAsync(summary),
        summary => summary?.Group is not null && !IsBusy);

    public RelayCommand OpenSchedulePlanFormCommand => _openSchedulePlanFormCommand ??= new RelayCommand(
        () =>
        {
            ClearSchedulePlanForm();
            IsSchedulePlanFormVisible = true;
        },
        () => !IsBusy);

    public RelayCommand CloseSchedulePlanFormCommand => _closeSchedulePlanFormCommand ??= new RelayCommand(
        () => IsSchedulePlanFormVisible = false,
        () => !IsBusy);

    public RelayCommand<string> SelectReportCommand => _selectReportCommand ??= new RelayCommand<string>(
        reportName => SelectedReportName = string.IsNullOrWhiteSpace(reportName) ? ReportOptions[0] : reportName);

    public RelayCommand ClearPageErrorCommand => _clearPageErrorCommand ??= new RelayCommand(() => PageErrorMessage = string.Empty);

    public RelayCommand ClearLogFiltersCommand => _clearLogFiltersCommand ??= new RelayCommand(ClearLogFilters);

    public AsyncRelayCommand InstallWorkerServiceCommand => _installWorkerServiceCommand ??= new AsyncRelayCommand(
        InstallWorkerServiceAsync,
        CanControlWorkerService);

    public AsyncRelayCommand UninstallWorkerServiceCommand => _uninstallWorkerServiceCommand ??= new AsyncRelayCommand(
        UninstallWorkerServiceAsync,
        CanControlWorkerService);

    public bool IsDeviceDetailsSection => CurrentSection == SectionDeviceDetails;

    public bool IsLiveStatusSection => CurrentSection == SectionLiveStatus;

    public bool IsWorkerServiceSection => CurrentSection == SectionWorkerService;

    public bool IsSystemHealthSection => CurrentSection == SectionSystemHealth || IsReadinessSection;

    public bool IsHelpSection => CurrentSection == SectionHelp;

    public bool IsAboutSection => CurrentSection == SectionAbout;

    public bool IsLiveStatusNavSelected => IsLiveStatusSection;

    public bool IsWorkerServiceNavSelected => IsWorkerServiceSection;

    public bool IsSystemHealthNavSelected => IsSystemHealthSection;

    public bool IsHelpNavSelected => IsHelpSection;

    public bool IsAboutNavSelected => IsAboutSection;

    public string BreadcrumbText => IsDeviceDetailsSection && SelectedDevice is not null
        ? $"Cihazlar > {SelectedDevice.Name}"
        : SectionTitle;

    public string PageErrorMessage
    {
        get => _pageErrorMessage;
        set
        {
            if (SetProperty(ref _pageErrorMessage, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasPageError));
                OnPropertyChanged(nameof(PageErrorText));
            }
        }
    }

    public bool HasPageError => !string.IsNullOrWhiteSpace(PageErrorMessage);

    public string PageErrorText => string.IsNullOrWhiteSpace(PageErrorMessage)
        ? "İçerik yüklenemedi."
        : PageErrorMessage;

    public string PageLoadingText => CurrentSection switch
    {
        SectionDevices => "Cihazlar yükleniyor...",
        SectionLogs => "Ping geçmişi yükleniyor...",
        SectionNotifications => "Bildirimler yükleniyor...",
        SectionWorkerService => "Worker durumu yükleniyor...",
        SectionSystemHealth => "Sistem sağlığı yükleniyor...",
        _ => "İçerik yükleniyor..."
    };

    public string PageEmptyText => CurrentSection switch
    {
        SectionDevices => "Henüz cihaz eklenmemiş.",
        SectionGroups => "Henüz cihaz grubu oluşturulmamış.",
        SectionLogs => "Seçili filtrelerle ping kaydı bulunamadı.",
        SectionNotifications => "Bildirim geçmişi boş.",
        SectionEvents => "Açık kesinti bulunmuyor.",
        _ => "Gösterilecek kayıt bulunamadı."
    };

    public PingLog? SelectedLog
    {
        get => _selectedLog;
        set
        {
            if (SetProperty(ref _selectedLog, value))
            {
                OnPropertyChanged(nameof(HasSelectedLog));
            }
        }
    }

    public bool HasSelectedLog => SelectedLog is not null;

    public Outage? SelectedOutage
    {
        get => _selectedOutage;
        set
        {
            if (SetProperty(ref _selectedOutage, value))
            {
                OnPropertyChanged(nameof(HasSelectedOutage));
            }
        }
    }

    public bool HasSelectedOutage => SelectedOutage is not null;

    public string LiveGroupFilter
    {
        get => _liveGroupFilter;
        set
        {
            if (SetProperty(ref _liveGroupFilter, value ?? AllGroupsText))
            {
                RefreshLiveStatusCollections();
            }
        }
    }

    public string LiveTypeFilter
    {
        get => _liveTypeFilter;
        set
        {
            if (SetProperty(ref _liveTypeFilter, value ?? AllDeviceTypesText))
            {
                RefreshLiveStatusCollections();
            }
        }
    }

    public string LiveStatusFilter
    {
        get => _liveStatusFilter;
        set
        {
            if (SetProperty(ref _liveStatusFilter, value ?? AllStatusesText))
            {
                RefreshLiveStatusCollections();
            }
        }
    }

    public bool IsSchedulePlanFormVisible
    {
        get => _isSchedulePlanFormVisible;
        set
        {
            if (SetProperty(ref _isSchedulePlanFormVisible, value))
            {
                OnPropertyChanged(nameof(IsAnyDialogVisible));
            }
        }
    }

    public string SelectedReportName
    {
        get => _selectedReportName;
        set => SetProperty(ref _selectedReportName, string.IsNullOrWhiteSpace(value) ? ReportOptions[0] : value);
    }

    public DateTime? ReportStartDate
    {
        get => _reportStartDate;
        set => SetProperty(ref _reportStartDate, value);
    }

    public DateTime? ReportEndDate
    {
        get => _reportEndDate;
        set => SetProperty(ref _reportEndDate, value);
    }

    public int? ReportDeviceFilterId
    {
        get => _reportDeviceFilterId;
        set => SetProperty(ref _reportDeviceFilterId, value);
    }

    public int? ReportGroupFilterId
    {
        get => _reportGroupFilterId;
        set => SetProperty(ref _reportGroupFilterId, value);
    }

    public bool HasSelectedDeviceDetails => SelectedDevice is not null;

    public bool HasNoSelectedDeviceDetails => SelectedDevice is null;

    public bool HasGroups => DeviceGroupSummaries.Count > 0;

    public bool HasNoGroups => DeviceGroupSummaries.Count == 0;

    public bool HasLogs => Logs.Count > 0;

    public bool HasNoLogs => Logs.Count == 0;

    public bool HasOpenOutages => OpenOutages.Count > 0;

    public bool HasNoOpenOutages => OpenOutages.Count == 0;

    public bool HasNotifications => NotificationOutboxItems.Count > 0;

    public bool HasNoNotifications => NotificationOutboxItems.Count == 0;

    public bool HasRecentAlerts => RecentAlerts.Count > 0;

    public bool HasNoRecentAlerts => RecentAlerts.Count == 0;

    public bool HasSchedulePlans => SchedulePlans.Count > 0;

    public bool HasNoSchedulePlans => SchedulePlans.Count == 0;

    public int TotalDeviceCount => Devices.Count(device => !device.IsDeleted);

    public int OnlineDeviceCount => Devices.Count(device => !device.IsDeleted && device.LastStatus == DeviceStatus.Online);

    public int PendingCheckDeviceCount => Devices.Count(device => !device.IsDeleted && device.LastStatus == DeviceStatus.Unknown);

    public int CheckedDeviceCount => Devices.Count(device => !device.IsDeleted && device.LastCheckedAt.HasValue);

    public int OpenIncidentCount => OpenOutages.Count;

    public string UiStatusText => IsBusy ? "Yükleniyor" : "Hazır";

    public string TrayStatusText => "Tray ayrı uygulama olarak çalışır.";

    public string SQLiteStatusText => File.Exists(DatabaseLocation) ? "Bağlı" : "Bulunamadı";

    public string DatabaseFileSizeText
    {
        get
        {
            if (!File.Exists(DatabaseLocation))
            {
                return "-";
            }

            var size = new FileInfo(DatabaseLocation).Length;
            return size < 1024 * 1024
                ? $"{size / 1024d:0.0} KB"
                : $"{size / 1024d / 1024d:0.0} MB";
        }
    }

    public string DatabaseWalStatusText => File.Exists(DatabaseLocation + "-wal") ? "Aktif" : "Kapalı";

    public string DatabaseBackupText => Directory.Exists(DatabasePaths.BackupDirectory)
        ? DatabasePaths.BackupDirectory
        : "-";

    public string LastMigrationText => "Uygulama başlangıcında doğrulanır";

    public string WorkerInstallationPath => ResolveWorkerExecutablePath();

    public string WorkerServiceNavigationText => "Worker Servisi";

    public string WorkerServiceNavigationAutomationName => "Worker Servisi sayfasını aç";

    public string WorkerServiceQuickActionText => "Worker'ı Başlat / Durdur";

    public string WorkerServiceQuickActionAutomationName => "Worker servisi sayfasını aç";

    public string WorkerServiceDescription => "Worker, cihaz kontrollerini arka planda gerçekleştirir. Yönetim paneli kapalı olsa bile çalışmaya devam edebilir.";

    public string WorkerServiceOperationsTitle => "Worker işlemleri";

    public string WorkerServiceStatusLabel => "Worker durumu";

    public string WorkerAutostartText => "Windows açıldığında Worker'ı otomatik başlat";

    private void NavigateToSection(string section)
    {
        IsDeviceFormVisible = false;
        CurrentSection = section;
        NotifyExtendedNavigationState();
    }

    private void OpenDeviceDetails(Device? device)
    {
        if (device is null)
        {
            return;
        }

        SelectedDevice = device;
        IsDeviceFormVisible = false;
        CurrentSection = SectionDeviceDetails;
        RefreshSelectedDeviceCollections();
        NotifyExtendedNavigationState();
    }

    private void OpenGroupDetails(DeviceGroupSummaryViewModel? summary)
    {
        if (summary?.Group is null)
        {
            return;
        }

        SelectedGroup = summary.Group;
        RefreshSelectedGroupDevices();
    }

    private async Task PingGroupFromSummaryAsync(DeviceGroupSummaryViewModel summary)
    {
        if (summary.Group is null)
        {
            return;
        }

        SelectedGroup = summary.Group;
        await PingGroupAsync(summary.Group);
    }

    private async Task InstallWorkerServiceAsync()
    {
        await RunWorkerServiceOperationAsync(
            () => _windowsServiceStatusService.InstallAsync(),
            "Worker kurulumu başlatılıyor...",
            showWarningOnFailure: true);
    }

    private async Task UninstallWorkerServiceAsync()
    {
        if (!_dialogService.Confirm("Worker kaldırılsın mı?", "Worker servisi Windows servislerinden kaldırılacak. Veriler korunur."))
        {
            return;
        }

        await RunWorkerServiceOperationAsync(
            () => _windowsServiceStatusService.UninstallAsync(),
            "Worker kaldırılıyor...",
            showWarningOnFailure: true);
    }

    private void NotifyExtendedNavigationState()
    {
        OnPropertyChanged(nameof(IsDeviceDetailsSection));
        OnPropertyChanged(nameof(IsLiveStatusSection));
        OnPropertyChanged(nameof(IsWorkerServiceSection));
        OnPropertyChanged(nameof(IsSystemHealthSection));
        OnPropertyChanged(nameof(IsHelpSection));
        OnPropertyChanged(nameof(IsAboutSection));
        OnPropertyChanged(nameof(IsLiveStatusNavSelected));
        OnPropertyChanged(nameof(IsWorkerServiceNavSelected));
        OnPropertyChanged(nameof(IsSystemHealthNavSelected));
        OnPropertyChanged(nameof(IsHelpNavSelected));
        OnPropertyChanged(nameof(IsAboutNavSelected));
        OnPropertyChanged(nameof(BreadcrumbText));
        OnPropertyChanged(nameof(PageLoadingText));
        OnPropertyChanged(nameof(PageEmptyText));
        OpenSelectedDeviceDetailsCommand.NotifyCanExecuteChanged();
    }

    private void NotifyFocusedPageState()
    {
        RefreshOverviewCards();
        RefreshDashboardAlerts();
        RefreshGroupSummaries();
        RefreshSelectedGroupDevices();
        RefreshLiveStatusCollections();
        RefreshSelectedDeviceCollections();
        OnPropertyChanged(nameof(HasGroups));
        OnPropertyChanged(nameof(HasNoGroups));
        OnPropertyChanged(nameof(HasLogs));
        OnPropertyChanged(nameof(HasNoLogs));
        OnPropertyChanged(nameof(HasOpenOutages));
        OnPropertyChanged(nameof(HasNoOpenOutages));
        OnPropertyChanged(nameof(HasNotifications));
        OnPropertyChanged(nameof(HasNoNotifications));
        OnPropertyChanged(nameof(HasRecentAlerts));
        OnPropertyChanged(nameof(HasNoRecentAlerts));
        OnPropertyChanged(nameof(HasSchedulePlans));
        OnPropertyChanged(nameof(HasNoSchedulePlans));
        OnPropertyChanged(nameof(TotalDeviceCount));
        OnPropertyChanged(nameof(OnlineDeviceCount));
        OnPropertyChanged(nameof(PendingCheckDeviceCount));
        OnPropertyChanged(nameof(CheckedDeviceCount));
        OnPropertyChanged(nameof(OpenIncidentCount));
        OnPropertyChanged(nameof(UiStatusText));
        OnPropertyChanged(nameof(SQLiteStatusText));
        OnPropertyChanged(nameof(DatabaseFileSizeText));
        OnPropertyChanged(nameof(DatabaseWalStatusText));
    }

    private void RefreshOverviewCards()
    {
        if (SummaryCards.Count < 6)
        {
            EnsureSummaryCards();
        }

        if (SummaryCards.Count >= 6)
        {
            SummaryCards[5].Value = WorkerHealthText;
        }

        if (OverviewSummaryCards.Count != 6 || !OverviewSummaryCards.SequenceEqual(SummaryCards.Take(6)))
        {
            ReplaceCollection(OverviewSummaryCards, SummaryCards.Take(6));
        }
    }

    private void RefreshDashboardAlerts()
    {
        var alerts = new List<DashboardAlertViewModel>();
        alerts.AddRange(OpenOutages
            .OrderByDescending(outage => outage.StartedAt)
            .Take(3)
            .Select(outage => new DashboardAlertViewModel
            {
                Event = "Yeni erişilemeyen cihaz",
                Detail = $"{outage.DeviceName} ({outage.IpAddress})",
                TimeText = outage.StartedAtText,
                Severity = "Hata"
            }));

        alerts.AddRange(Logs
            .Where(log => log.Status == DeviceStatus.Online)
            .OrderByDescending(log => log.CheckedAt)
            .Take(2)
            .Select(log => new DashboardAlertViewModel
            {
                Event = "Yeniden çevrimiçi",
                Detail = $"{log.DeviceName} ({log.IpAddress})",
                TimeText = log.CheckedAtText,
                Severity = "Bilgi"
            }));

        if (WorkerHealthText.Contains("Hata", StringComparison.OrdinalIgnoreCase)
            || WorkerHealthText.Contains("Gecik", StringComparison.OrdinalIgnoreCase))
        {
            alerts.Add(new DashboardAlertViewModel
            {
                Event = "Worker hatası",
                Detail = WorkerHealthText,
                TimeText = WorkerLastSeenAtText,
                Severity = "Uyarı"
            });
        }

        if (FailedNotificationCount > 0)
        {
            alerts.Add(new DashboardAlertViewModel
            {
                Event = "Bildirim hatası",
                Detail = $"{FailedNotificationCount} başarısız bildirim",
                TimeText = NotificationLastSuccessfulAtText,
                Severity = "Uyarı"
            });
        }

        ReplaceCollection(RecentAlerts, alerts
            .OrderByDescending(alert => DateTime.TryParseExact(alert.TimeText, "dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsed) ? parsed : DateTime.MinValue)
            .Take(5));
        OnPropertyChanged(nameof(HasRecentAlerts));
        OnPropertyChanged(nameof(HasNoRecentAlerts));
    }

    private void ClearLogFilters()
    {
        LogStartDate = null;
        LogEndDate = null;
        LogDeviceNameFilter = string.Empty;
        LogIpAddressFilter = string.Empty;
        LogDeviceTypeFilter = AllDeviceTypesText;
        LogStatusFilter = AllStatusesText;
        LogGroupFilter = AllGroupsText;
        LogTriggerFilter = AllTriggersText;
        LogPlanNameFilter = string.Empty;
        LogOnlyUnreachable = false;
    }

    private void RefreshGroupSummaries()
    {
        var groups = DeviceGroups
            .OrderBy(group => group.Name)
            .Select(group =>
            {
                var devices = Devices
                    .Where(device => !device.IsDeleted && (device.GroupId == group.Id || string.Equals(device.GroupName, group.Name, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                return new DeviceGroupSummaryViewModel
                {
                    Group = group,
                    Name = group.Name,
                    DeviceCount = devices.Count,
                    OnlineCount = devices.Count(device => device.LastStatus == DeviceStatus.Online),
                    OfflineCount = devices.Count(device => device.LastStatus == DeviceStatus.Offline)
                };
            });

        ReplaceCollection(DeviceGroupSummaries, groups);
    }

    private void RefreshSelectedGroupDevices()
    {
        if (SelectedGroup is null)
        {
            ReplaceCollection(SelectedGroupDevices, Array.Empty<Device>());
            return;
        }

        ReplaceCollection(SelectedGroupDevices, Devices
            .Where(device => !device.IsDeleted && (device.GroupId == SelectedGroup.Id || string.Equals(device.GroupName, SelectedGroup.Name, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(device => device.Name));
    }

    private void RefreshLiveStatusCollections()
    {
        var devices = Devices
            .Where(device => !device.IsDeleted)
            .Where(MatchesLiveFilters)
            .OrderBy(device => device.Name)
            .ToList();

        ReplaceCollection(LiveOnlineDevices, devices.Where(device => device.LastStatus == DeviceStatus.Online));
        ReplaceCollection(LiveOfflineDevices, devices.Where(device => device.LastStatus == DeviceStatus.Offline));
        ReplaceCollection(LiveCheckedDevices, devices.Where(device => device.LastStatus == DeviceStatus.Checking || device.LastCheckedAt.HasValue).Take(100));
        ReplaceCollection(LiveRecentChanges, Logs
            .Where(log => devices.Any(device => device.Id == log.DeviceId))
            .OrderByDescending(log => log.CheckedAt)
            .Take(12));
    }

    private bool MatchesLiveFilters(Device device)
    {
        if (LiveGroupFilter != AllGroupsText && !string.Equals(device.GroupName, LiveGroupFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var type = ParseDeviceTypeFilter(LiveTypeFilter);
        if (type.HasValue && device.DeviceType != type.Value)
        {
            return false;
        }

        var status = ParseStatusFilter(LiveStatusFilter);
        return !status.HasValue || device.LastStatus == status.Value;
    }

    private void RefreshSelectedDeviceCollections()
    {
        if (SelectedDevice is null)
        {
            ReplaceCollection(SelectedDevicePingLogs, Array.Empty<PingLog>());
            ReplaceCollection(SelectedDeviceOutages, Array.Empty<Outage>());
            ReplaceCollection(SelectedDeviceNotifications, Array.Empty<NotificationOutboxItem>());
            return;
        }

        ReplaceCollection(SelectedDevicePingLogs, Logs
            .Where(log => log.DeviceId == SelectedDevice.Id)
            .OrderByDescending(log => log.CheckedAt)
            .Take(200));
        ReplaceCollection(SelectedDeviceOutages, RecentOutages
            .Where(outage => outage.DeviceId == SelectedDevice.Id)
            .OrderByDescending(outage => outage.StartedAt)
            .Take(200));
        ReplaceCollection(SelectedDeviceNotifications, NotificationOutboxItems
            .Where(item => item.DeviceId == SelectedDevice.Id)
            .OrderByDescending(item => item.CreatedAtUtc)
            .Take(200));
    }

    private static string ResolveWorkerExecutablePath()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "NetworkHealthMonitor.Worker.exe"),
            Path.Combine(baseDirectory, "Worker", "NetworkHealthMonitor.Worker.exe"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "NetworkHealthMonitor.Worker", "bin", "Release", "net10.0-windows", "NetworkHealthMonitor.Worker.exe")),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "NetworkHealthMonitor.Worker", "bin", "Debug", "net10.0-windows", "NetworkHealthMonitor.Worker.exe"))
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static void OpenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }
}
