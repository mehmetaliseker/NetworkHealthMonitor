using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Threading;
using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Infrastructure;
using NetworkHealthMonitor.Models;
using NetworkHealthMonitor.Services;

namespace NetworkHealthMonitor.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const string SectionDashboard = "Dashboard";
    private const string SectionDevices = "Cihazlar";
    private const string SectionLogs = "Loglar";
    private const string SectionSettings = "Ayarlar";
    private const string AllDeviceTypesText = "Tüm Tipler";
    private const string AllStatusesText = "Tüm Durumlar";
    private const string AllLocationsText = "Tüm Lokasyonlar";
    private const string AllGroupsText = "Tüm Gruplar";
    private const string AllCriticalText = "Tüm Cihazlar";
    private const string CriticalOnlyText = "Sadece kritik";
    private const string NonCriticalOnlyText = "Kritik olmayan";
    private const string SkipExistingImportText = "Var olanları atla";
    private const string UpdateExistingImportText = "Var olanları güncelle";

    private readonly DeviceRepository _deviceRepository;
    private readonly PingLogRepository _pingLogRepository;
    private readonly IPingService _pingService;
    private readonly CsvExportService _csvExportService;
    private readonly AppSettingsService _settingsService;
    private readonly IDialogService _dialogService;
    private readonly DataMaintenanceService _maintenanceService;
    private readonly DispatcherTimer _autoCheckTimer;
    private CancellationTokenSource? _pingCancellationTokenSource;
    private bool _isInitialized;
    private bool _isBusy;
    private bool _isPinging;
    private bool _suppressDeviceViewRefresh;
    private string _currentSection = SectionDashboard;
    private string _statusMessage = "Başlatılıyor...";
    private Device? _selectedDevice;
    private int? _editingDeviceId;
    private string _formName = string.Empty;
    private string _formIpAddress = string.Empty;
    private DeviceType _formDeviceType = DeviceType.Camera;
    private string _formLocation = string.Empty;
    private string _formGroupName = string.Empty;
    private bool _formIsCritical;
    private string _formDescription = string.Empty;
    private string _deviceSearchText = string.Empty;
    private string _deviceTypeFilter = AllDeviceTypesText;
    private string _deviceStatusFilter = AllStatusesText;
    private string _locationFilter = AllLocationsText;
    private string _groupFilter = AllGroupsText;
    private string _criticalFilter = AllCriticalText;
    private DateTime? _logStartDate;
    private DateTime? _logEndDate;
    private string _logDeviceNameFilter = string.Empty;
    private string _logIpAddressFilter = string.Empty;
    private string _logStatusFilter = AllStatusesText;
    private string _logDeviceTypeFilter = AllDeviceTypesText;
    private bool _logOnlyUnreachable;
    private int _pingTimeoutMs = 1000;
    private int _maxParallelPings = 32;
    private bool _autoCheckEnabled;
    private int _autoCheckIntervalMinutes = 5;
    private string _csvDelimiter = ";";
    private int _logRetentionDays = 30;
    private int _failedLast24Hours;
    private int _pingTotalCount;
    private int _pingCompletedCount;
    private int _pingSuccessCount;
    private int _pingFailureCount;
    private CsvImportPreview? _pendingImportPreview;
    private string _selectedImportDuplicateAction = SkipExistingImportText;
    private string _overallUptimeText = "-";
    private string _criticalUptimeText = "-";

    public MainViewModel(
        DeviceRepository deviceRepository,
        PingLogRepository pingLogRepository,
        IPingService pingService,
        CsvExportService csvExportService,
        AppSettingsService settingsService,
        IDialogService dialogService,
        DataMaintenanceService maintenanceService)
    {
        _deviceRepository = deviceRepository;
        _pingLogRepository = pingLogRepository;
        _pingService = pingService;
        _csvExportService = csvExportService;
        _settingsService = settingsService;
        _dialogService = dialogService;
        _maintenanceService = maintenanceService;

        DeviceTypeOptions = new ObservableCollection<DeviceTypeOption>(DeviceTypeOption.CreateAll());
        DeviceTypeFilterOptions = new ObservableCollection<string>(new[] { AllDeviceTypesText }.Concat(DeviceTypeOptions.Select(option => option.Label)));
        DeviceStatusFilterOptions = new ObservableCollection<string>(new[]
        {
            AllStatusesText,
            DeviceStatus.Reachable.ToDisplayName(),
            DeviceStatus.Unreachable.ToDisplayName(),
            DeviceStatus.NotChecked.ToDisplayName(),
            DeviceStatus.Checking.ToDisplayName()
        });
        LogStatusOptions = new ObservableCollection<string>(DeviceStatusFilterOptions.Where(value => value != DeviceStatus.Checking.ToDisplayName()));
        LogDeviceTypeOptions = new ObservableCollection<string>(DeviceTypeFilterOptions);
        LocationFilterOptions = new ObservableCollection<string>(new[] { AllLocationsText });
        GroupFilterOptions = new ObservableCollection<string>(new[] { AllGroupsText });
        CriticalFilterOptions = new ObservableCollection<string>(new[] { AllCriticalText, CriticalOnlyText, NonCriticalOnlyText });
        ImportDuplicateActionOptions = new ObservableCollection<string>(new[] { SkipExistingImportText, UpdateExistingImportText });

        DevicesView = CollectionViewSource.GetDefaultView(Devices);
        DevicesView.Filter = FilterDevice;
        DevicesView.SortDescriptions.Add(new SortDescription(nameof(Device.Name), ListSortDirection.Ascending));
        DevicesView.SortDescriptions.Add(new SortDescription(nameof(Device.IpSortKey), ListSortDirection.Ascending));

        LogsView = CollectionViewSource.GetDefaultView(Logs);
        LogsView.Filter = FilterLog;
        LogsView.SortDescriptions.Add(new SortDescription(nameof(PingLog.CheckedAt), ListSortDirection.Descending));

        Devices.CollectionChanged += DevicesCollectionChanged;
        Logs.CollectionChanged += (_, _) => RaiseCommandStates();

        NavigateDashboardCommand = new RelayCommand(() => CurrentSection = SectionDashboard);
        NavigateDevicesCommand = new RelayCommand(() => CurrentSection = SectionDevices);
        NavigateLogsCommand = new RelayCommand(() => CurrentSection = SectionLogs);
        NavigateSettingsCommand = new RelayCommand(() => CurrentSection = SectionSettings);

        SaveDeviceCommand = new AsyncRelayCommand(SaveDeviceAsync, () => !IsBusy);
        EditSelectedDeviceCommand = new RelayCommand(StartEditSelectedDevice, () => SelectedDevice is not null && !IsBusy);
        DeleteSelectedDeviceCommand = new AsyncRelayCommand(DeleteSelectedDeviceAsync, () => SelectedDevice is not null && !IsBusy);
        ClearFormCommand = new RelayCommand(ClearForm, () => !IsBusy);
        PingAllCommand = new AsyncRelayCommand(
            () => PingDevicesAsync(Devices.ToList(), "Tüm cihazlar kontrol ediliyor..."),
            () => Devices.Count > 0 && !IsBusy);
        PingFilteredDevicesCommand = new AsyncRelayCommand(
            () => PingDevicesAsync(DevicesView.Cast<Device>().ToList(), "Filtrelenmiş cihazlar kontrol ediliyor..."),
            () => DevicesView.Cast<Device>().Any() && !IsBusy);
        PingCamerasCommand = new AsyncRelayCommand(
            () => PingByTypeAsync(DeviceType.Camera),
            () => Devices.Any(device => device.DeviceType == DeviceType.Camera) && !IsBusy);
        PingAccessPointsCommand = new AsyncRelayCommand(
            () => PingByTypeAsync(DeviceType.AccessPoint),
            () => Devices.Any(device => device.DeviceType == DeviceType.AccessPoint) && !IsBusy);
        PingComputersCommand = new AsyncRelayCommand(
            () => PingByTypeAsync(DeviceType.Computer),
            () => Devices.Any(device => device.DeviceType == DeviceType.Computer) && !IsBusy);
        PingSwitchesCommand = new AsyncRelayCommand(
            () => PingByTypeAsync(DeviceType.Switch),
            () => Devices.Any(device => device.DeviceType == DeviceType.Switch) && !IsBusy);
        PingOthersCommand = new AsyncRelayCommand(
            () => PingByTypeAsync(DeviceType.Other),
            () => Devices.Any(device => device.DeviceType == DeviceType.Other) && !IsBusy);
        PingSelectedDeviceCommand = new AsyncRelayCommand(
            () => SelectedDevice is null
                ? Task.CompletedTask
                : PingDevicesAsync(new[] { SelectedDevice }, "Seçili cihaz kontrol ediliyor..."),
            () => SelectedDevice is not null && !IsBusy);
        PingDeviceCommand = new AsyncRelayCommand<Device>(
            device => device is null ? Task.CompletedTask : PingDevicesAsync(new[] { device }, "Seçili cihaz kontrol ediliyor..."),
            device => device is not null && !IsBusy);
        EditDeviceCommand = new RelayCommand<Device>(
            device =>
            {
                if (device is null)
                {
                    return;
                }

                SelectedDevice = device;
                StartEditSelectedDevice();
            },
            device => device is not null && !IsBusy);
        DeleteDeviceCommand = new AsyncRelayCommand<Device>(
            async device =>
            {
                if (device is null)
                {
                    return;
                }

                SelectedDevice = device;
                await DeleteSelectedDeviceAsync();
            },
            device => device is not null && !IsBusy);
        CancelPingCommand = new RelayCommand(CancelPing, () => IsPinging);
        RefreshLogsCommand = new AsyncRelayCommand(() => LoadLogsAsync(), () => !IsBusy);
        ShowSelectedDeviceLogsCommand = new AsyncRelayCommand(ShowSelectedDeviceLogsAsync, () => SelectedDevice is not null && !IsBusy);
        ShowDeviceLogsCommand = new AsyncRelayCommand<Device>(
            async device =>
            {
                if (device is null)
                {
                    return;
                }

                SelectedDevice = device;
                await ShowSelectedDeviceLogsAsync();
            },
            device => device is not null && !IsBusy);
        ClearLogsCommand = new AsyncRelayCommand(ClearLogsAsync, () => Logs.Count > 0 && !IsBusy);
        ClearOldLogsCommand = new AsyncRelayCommand(ClearOldLogsAsync, () => !IsBusy && LogRetentionDays > 0);
        ExportDevicesCommand = new AsyncRelayCommand(ExportDevicesAsync, () => Devices.Count > 0 && !IsBusy);
        ImportDevicesCommand = new AsyncRelayCommand(ImportDevicesAsync, () => !IsBusy);
        ApplyImportPreviewCommand = new AsyncRelayCommand(ApplyImportPreviewAsync, () => PendingImportPreview is not null && PendingImportPreview.HasImportableRows && !IsBusy);
        CancelImportPreviewCommand = new RelayCommand(CancelImportPreview, () => PendingImportPreview is not null && !IsBusy);
        ExportImportErrorsCommand = new AsyncRelayCommand(ExportImportErrorsAsync, () => PendingImportPreview?.InvalidRowCount > 0 && !IsBusy);
        CreateCsvTemplateCommand = new AsyncRelayCommand(CreateCsvTemplateAsync, () => !IsBusy);
        CopyDeviceIpCommand = new RelayCommand<Device>(
            device =>
            {
                if (device is null)
                {
                    return;
                }

                _dialogService.CopyToClipboard(device.IpAddress);
                StatusMessage = "IP adresi panoya kopyalandı.";
            },
            device => device is not null);
        ExportSingleDeviceCommand = new AsyncRelayCommand<Device>(
            device => device is null ? Task.CompletedTask : ExportSingleDeviceAsync(device),
            device => device is not null && !IsBusy);
        ExportLogsCommand = new AsyncRelayCommand(ExportLogsAsync, () => Logs.Count > 0 && !IsBusy);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, () => !IsBusy);
        BackupDatabaseCommand = new AsyncRelayCommand(BackupDatabaseAsync, () => !IsBusy);
        RestoreDatabaseCommand = new AsyncRelayCommand(RestoreDatabaseAsync, () => !IsBusy);
        ExportSettingsCommand = new AsyncRelayCommand(ExportSettingsAsync, () => !IsBusy);
        ImportSettingsCommand = new AsyncRelayCommand(ImportSettingsAsync, () => !IsBusy);

        _autoCheckTimer = new DispatcherTimer();
        _autoCheckTimer.Tick += AutoCheckTimerTick;

        EnsureSummaryCards();
        UpdateSummaryCards();
    }

    public ObservableCollection<Device> Devices { get; } = new();

    public ObservableCollection<PingLog> Logs { get; } = new();

    public ICollectionView DevicesView { get; }

    public ICollectionView LogsView { get; }

    public ObservableCollection<DeviceTypeOption> DeviceTypeOptions { get; }

    public ObservableCollection<string> DeviceTypeFilterOptions { get; }

    public ObservableCollection<string> DeviceStatusFilterOptions { get; }

    public ObservableCollection<string> LocationFilterOptions { get; }

    public ObservableCollection<string> GroupFilterOptions { get; }

    public ObservableCollection<string> CriticalFilterOptions { get; }

    public ObservableCollection<string> LogStatusOptions { get; }

    public ObservableCollection<string> LogDeviceTypeOptions { get; }

    public ObservableCollection<string> ImportDuplicateActionOptions { get; }

    public ObservableCollection<CsvImportPreviewRow> ImportPreviewRows { get; } = new();

    public ObservableCollection<SummaryCardViewModel> SummaryCards { get; } = new();

    public ObservableCollection<SummaryCardViewModel> DeviceTypeDistribution { get; } = new();

    public ObservableCollection<Device> RecentUnreachableDevices { get; } = new();

    public ObservableCollection<Device> HighLatencyDevices { get; } = new();

    public ObservableCollection<Device> CriticalProblemDevices { get; } = new();

    public ObservableCollection<Device> MostProblematicDevices { get; } = new();

    public RelayCommand NavigateDashboardCommand { get; }

    public RelayCommand NavigateDevicesCommand { get; }

    public RelayCommand NavigateLogsCommand { get; }

    public RelayCommand NavigateSettingsCommand { get; }

    public AsyncRelayCommand SaveDeviceCommand { get; }

    public RelayCommand EditSelectedDeviceCommand { get; }

    public AsyncRelayCommand DeleteSelectedDeviceCommand { get; }

    public RelayCommand ClearFormCommand { get; }

    public AsyncRelayCommand PingAllCommand { get; }

    public AsyncRelayCommand PingFilteredDevicesCommand { get; }

    public AsyncRelayCommand PingCamerasCommand { get; }

    public AsyncRelayCommand PingAccessPointsCommand { get; }

    public AsyncRelayCommand PingComputersCommand { get; }

    public AsyncRelayCommand PingSwitchesCommand { get; }

    public AsyncRelayCommand PingOthersCommand { get; }

    public AsyncRelayCommand PingSelectedDeviceCommand { get; }

    public AsyncRelayCommand<Device> PingDeviceCommand { get; }

    public RelayCommand<Device> EditDeviceCommand { get; }

    public AsyncRelayCommand<Device> DeleteDeviceCommand { get; }

    public RelayCommand CancelPingCommand { get; }

    public AsyncRelayCommand RefreshLogsCommand { get; }

    public AsyncRelayCommand ShowSelectedDeviceLogsCommand { get; }

    public AsyncRelayCommand<Device> ShowDeviceLogsCommand { get; }

    public AsyncRelayCommand ClearLogsCommand { get; }

    public AsyncRelayCommand ClearOldLogsCommand { get; }

    public AsyncRelayCommand ExportDevicesCommand { get; }

    public AsyncRelayCommand ImportDevicesCommand { get; }

    public AsyncRelayCommand ApplyImportPreviewCommand { get; }

    public RelayCommand CancelImportPreviewCommand { get; }

    public AsyncRelayCommand ExportImportErrorsCommand { get; }

    public AsyncRelayCommand CreateCsvTemplateCommand { get; }

    public RelayCommand<Device> CopyDeviceIpCommand { get; }

    public AsyncRelayCommand<Device> ExportSingleDeviceCommand { get; }

    public AsyncRelayCommand ExportLogsCommand { get; }

    public AsyncRelayCommand SaveSettingsCommand { get; }

    public AsyncRelayCommand BackupDatabaseCommand { get; }

    public AsyncRelayCommand RestoreDatabaseCommand { get; }

    public AsyncRelayCommand ExportSettingsCommand { get; }

    public AsyncRelayCommand ImportSettingsCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool IsPinging
    {
        get => _isPinging;
        private set
        {
            if (SetProperty(ref _isPinging, value))
            {
                OnPropertyChanged(nameof(CanShowPingProgress));
                RaiseCommandStates();
            }
        }
    }

    public bool CanShowPingProgress => IsPinging || PingTotalCount > 0;

    public string CurrentSection
    {
        get => _currentSection;
        set
        {
            if (SetProperty(ref _currentSection, value))
            {
                OnPropertyChanged(nameof(IsDashboardSection));
                OnPropertyChanged(nameof(IsDevicesSection));
                OnPropertyChanged(nameof(IsLogsSection));
                OnPropertyChanged(nameof(IsSettingsSection));
            }
        }
    }

    public bool IsDashboardSection => CurrentSection == SectionDashboard;

    public bool IsDevicesSection => CurrentSection == SectionDevices;

    public bool IsLogsSection => CurrentSection == SectionLogs;

    public bool IsSettingsSection => CurrentSection == SectionSettings;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public Device? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public int? EditingDeviceId
    {
        get => _editingDeviceId;
        private set
        {
            if (SetProperty(ref _editingDeviceId, value))
            {
                OnPropertyChanged(nameof(FormModeTitle));
                OnPropertyChanged(nameof(FormActionText));
            }
        }
    }

    public string FormModeTitle => EditingDeviceId.HasValue ? "Cihaz Düzenle" : "Yeni Cihaz";

    public string FormActionText => EditingDeviceId.HasValue ? "Güncelle" : "Kaydet";

    public string FormName
    {
        get => _formName;
        set => SetProperty(ref _formName, value);
    }

    public string FormIpAddress
    {
        get => _formIpAddress;
        set => SetProperty(ref _formIpAddress, value);
    }

    public DeviceType FormDeviceType
    {
        get => _formDeviceType;
        set => SetProperty(ref _formDeviceType, value);
    }

    public string FormLocation
    {
        get => _formLocation;
        set => SetProperty(ref _formLocation, value);
    }

    public string FormGroupName
    {
        get => _formGroupName;
        set => SetProperty(ref _formGroupName, value);
    }

    public bool FormIsCritical
    {
        get => _formIsCritical;
        set => SetProperty(ref _formIsCritical, value);
    }

    public string FormDescription
    {
        get => _formDescription;
        set => SetProperty(ref _formDescription, value);
    }

    public string DeviceSearchText
    {
        get => _deviceSearchText;
        set
        {
            if (SetProperty(ref _deviceSearchText, value))
            {
                DevicesView.Refresh();
                RaiseCommandStates();
            }
        }
    }

    public string DeviceTypeFilter
    {
        get => _deviceTypeFilter;
        set
        {
            if (SetProperty(ref _deviceTypeFilter, value))
            {
                DevicesView.Refresh();
                RaiseCommandStates();
            }
        }
    }

    public string DeviceStatusFilter
    {
        get => _deviceStatusFilter;
        set
        {
            if (SetProperty(ref _deviceStatusFilter, value))
            {
                DevicesView.Refresh();
                RaiseCommandStates();
            }
        }
    }

    public string LocationFilter
    {
        get => _locationFilter;
        set
        {
            if (SetProperty(ref _locationFilter, value))
            {
                DevicesView.Refresh();
                RaiseCommandStates();
            }
        }
    }

    public string GroupFilter
    {
        get => _groupFilter;
        set
        {
            if (SetProperty(ref _groupFilter, value))
            {
                DevicesView.Refresh();
                RaiseCommandStates();
            }
        }
    }

    public string CriticalFilter
    {
        get => _criticalFilter;
        set
        {
            if (SetProperty(ref _criticalFilter, value))
            {
                DevicesView.Refresh();
                RaiseCommandStates();
            }
        }
    }

    public DateTime? LogStartDate
    {
        get => _logStartDate;
        set
        {
            if (SetProperty(ref _logStartDate, value))
            {
                LogsView.Refresh();
            }
        }
    }

    public DateTime? LogEndDate
    {
        get => _logEndDate;
        set
        {
            if (SetProperty(ref _logEndDate, value))
            {
                LogsView.Refresh();
            }
        }
    }

    public string LogDeviceNameFilter
    {
        get => _logDeviceNameFilter;
        set
        {
            if (SetProperty(ref _logDeviceNameFilter, value))
            {
                LogsView.Refresh();
            }
        }
    }

    public string LogIpAddressFilter
    {
        get => _logIpAddressFilter;
        set
        {
            if (SetProperty(ref _logIpAddressFilter, value))
            {
                LogsView.Refresh();
            }
        }
    }

    public string LogStatusFilter
    {
        get => _logStatusFilter;
        set
        {
            if (SetProperty(ref _logStatusFilter, value))
            {
                LogsView.Refresh();
            }
        }
    }

    public string LogDeviceTypeFilter
    {
        get => _logDeviceTypeFilter;
        set
        {
            if (SetProperty(ref _logDeviceTypeFilter, value))
            {
                LogsView.Refresh();
            }
        }
    }

    public bool LogOnlyUnreachable
    {
        get => _logOnlyUnreachable;
        set
        {
            if (SetProperty(ref _logOnlyUnreachable, value))
            {
                LogsView.Refresh();
            }
        }
    }

    public int PingTimeoutMs
    {
        get => _pingTimeoutMs;
        set => SetProperty(ref _pingTimeoutMs, value);
    }

    public int MaxParallelPings
    {
        get => _maxParallelPings;
        set => SetProperty(ref _maxParallelPings, value);
    }

    public bool AutoCheckEnabled
    {
        get => _autoCheckEnabled;
        set => SetProperty(ref _autoCheckEnabled, value);
    }

    public int AutoCheckIntervalMinutes
    {
        get => _autoCheckIntervalMinutes;
        set => SetProperty(ref _autoCheckIntervalMinutes, value);
    }

    public string CsvDelimiter
    {
        get => _csvDelimiter;
        set => SetProperty(ref _csvDelimiter, value);
    }

    public int LogRetentionDays
    {
        get => _logRetentionDays;
        set
        {
            if (SetProperty(ref _logRetentionDays, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public int PingTotalCount
    {
        get => _pingTotalCount;
        private set
        {
            if (SetProperty(ref _pingTotalCount, value))
            {
                OnPropertyChanged(nameof(PingRemainingCount));
                OnPropertyChanged(nameof(PingProgressPercent));
                OnPropertyChanged(nameof(CanShowPingProgress));
            }
        }
    }

    public int PingCompletedCount
    {
        get => _pingCompletedCount;
        private set
        {
            if (SetProperty(ref _pingCompletedCount, value))
            {
                OnPropertyChanged(nameof(PingRemainingCount));
                OnPropertyChanged(nameof(PingProgressPercent));
            }
        }
    }

    public int PingRemainingCount => Math.Max(0, PingTotalCount - PingCompletedCount);

    public int PingSuccessCount
    {
        get => _pingSuccessCount;
        private set => SetProperty(ref _pingSuccessCount, value);
    }

    public int PingFailureCount
    {
        get => _pingFailureCount;
        private set => SetProperty(ref _pingFailureCount, value);
    }

    public double PingProgressPercent => PingTotalCount == 0 ? 0 : (double)PingCompletedCount / PingTotalCount * 100;

    public CsvImportPreview? PendingImportPreview
    {
        get => _pendingImportPreview;
        private set
        {
            if (SetProperty(ref _pendingImportPreview, value))
            {
                OnPropertyChanged(nameof(HasImportPreview));
                OnPropertyChanged(nameof(ImportPreviewSummaryText));
                RaiseCommandStates();
            }
        }
    }

    public bool HasImportPreview => PendingImportPreview is not null;

    public string SelectedImportDuplicateAction
    {
        get => _selectedImportDuplicateAction;
        set
        {
            if (SetProperty(ref _selectedImportDuplicateAction, value))
            {
                RefreshImportPreviewRows();
                OnPropertyChanged(nameof(ImportPreviewSummaryText));
            }
        }
    }

    public string ImportPreviewSummaryText => PendingImportPreview is null
        ? string.Empty
        : $"Toplam: {PendingImportPreview.TotalRows} | Eklenecek: {PendingImportPreview.AddCount} | Güncellenecek: {PendingImportPreview.UpdateCount} | Atlanacak: {PendingImportPreview.SkipCount} | Hatalı: {PendingImportPreview.InvalidRowCount}";

    public string OverallUptimeText
    {
        get => _overallUptimeText;
        private set => SetProperty(ref _overallUptimeText, value);
    }

    public string CriticalUptimeText
    {
        get => _criticalUptimeText;
        private set => SetProperty(ref _criticalUptimeText, value);
    }

    public bool HasCriticalProblems => CriticalProblemDevices.Count > 0;

    public string CriticalWarningText
    {
        get
        {
            if (CriticalProblemDevices.Count == 0)
            {
                return string.Empty;
            }

            var first = CriticalProblemDevices[0];
            var suffix = CriticalProblemDevices.Count > 1 ? $" ve {CriticalProblemDevices.Count - 1} cihaz daha" : string.Empty;
            return $"Kritik cihaz erişilemiyor: {first.Name} ({first.IpAddress}) - Son kontrol: {first.LastCheckedAtText}{suffix}";
        }
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Veriler yükleniyor...";

            var settings = await _settingsService.LoadAsync();
            ApplySettings(settings);

            var devices = await _deviceRepository.GetAllAsync();
            _suppressDeviceViewRefresh = true;
            Devices.Clear();
            foreach (var device in devices)
            {
                AddDeviceToCollection(device);
            }

            _suppressDeviceViewRefresh = false;
            RefreshFilterOptions();
            DevicesView.Refresh();

            await LoadLogsAsync(silent: true);
            await RefreshHealthMetricsAsync();
            UpdateAutoCheckTimer();
            StatusMessage = "Hazır";
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            StatusMessage = "Başlatma hatası";
            _dialogService.ShowError("Başlatma hatası", $"Uygulama verileri yüklenemedi.\n\n{ex.Message}");
        }
        finally
        {
            _suppressDeviceViewRefresh = false;
            IsBusy = false;
        }
    }

    private async Task SaveDeviceAsync()
    {
        var name = FormName.Trim();
        var ipAddress = FormIpAddress.Trim();
        var location = FormLocation.Trim();
        var groupName = FormGroupName.Trim();
        var description = FormDescription.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            _dialogService.ShowWarning("Eksik bilgi", "Cihaz adı zorunludur.");
            return;
        }

        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            _dialogService.ShowWarning("Eksik bilgi", "IP adresi zorunludur.");
            return;
        }

        if (!IpAddressValidator.IsValidIpv4(ipAddress))
        {
            _dialogService.ShowWarning("Geçersiz IP adresi", "Geçerli bir IPv4 adresi girin. Örnek: 192.168.1.10");
            return;
        }

        if (await _deviceRepository.ExistsByIpAsync(ipAddress, EditingDeviceId))
        {
            _dialogService.ShowWarning("Tekrarlanan IP", "Bu IP adresi zaten kayıtlı.");
            return;
        }

        try
        {
            StatusMessage = "Cihaz kaydediliyor...";
            var now = DateTime.Now;

            if (EditingDeviceId.HasValue)
            {
                var existingDevice = Devices.FirstOrDefault(device => device.Id == EditingDeviceId.Value);
                if (existingDevice is null)
                {
                    _dialogService.ShowWarning("Cihaz bulunamadı", "Düzenlenecek cihaz listede bulunamadı.");
                    ClearForm();
                    return;
                }

                var updatedDevice = new Device
                {
                    Id = existingDevice.Id,
                    Name = name,
                    IpAddress = ipAddress,
                    DeviceType = FormDeviceType,
                    Location = location,
                    GroupName = groupName,
                    IsCritical = FormIsCritical,
                    Description = description,
                    LastStatus = existingDevice.LastStatus == DeviceStatus.Checking ? DeviceStatus.NotChecked : existingDevice.LastStatus,
                    LastLatencyMs = existingDevice.LastLatencyMs,
                    LastCheckedAt = existingDevice.LastCheckedAt,
                    ConsecutiveFailures = existingDevice.ConsecutiveFailures,
                    ConsecutiveSuccesses = existingDevice.ConsecutiveSuccesses,
                    LastStableStatus = existingDevice.LastStableStatus,
                    CreatedAt = existingDevice.CreatedAt,
                    UpdatedAt = now
                };

                await _deviceRepository.UpdateAsync(updatedDevice);
                ApplyDeviceUpdate(existingDevice, updatedDevice);
                SelectedDevice = existingDevice;
                StatusMessage = "Cihaz güncellendi.";
            }
            else
            {
                var newDevice = new Device
                {
                    Name = name,
                    IpAddress = ipAddress,
                    DeviceType = FormDeviceType,
                    Location = location,
                    GroupName = groupName,
                    IsCritical = FormIsCritical,
                    Description = description,
                    LastStatus = DeviceStatus.NotChecked,
                    LastStableStatus = DeviceStatus.NotChecked,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                await _deviceRepository.AddAsync(newDevice);
                AddDeviceToCollection(newDevice);
                SelectedDevice = newDevice;
                StatusMessage = "Cihaz kaydedildi.";
            }

            ClearForm();
            RefreshFilterOptions();
            DevicesView.Refresh();
            UpdateSummaryCards();
            RaiseCommandStates();
        }
        catch (Exception ex)
        {
            StatusMessage = "Kayıt hatası";
            _dialogService.ShowError("Kayıt hatası", $"Cihaz kaydedilemedi.\n\n{ex.Message}");
        }
    }

    private void StartEditSelectedDevice()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        EditingDeviceId = SelectedDevice.Id;
        FormName = SelectedDevice.Name;
        FormIpAddress = SelectedDevice.IpAddress;
        FormDeviceType = SelectedDevice.DeviceType;
        FormLocation = SelectedDevice.Location;
        FormGroupName = SelectedDevice.GroupName;
        FormIsCritical = SelectedDevice.IsCritical;
        FormDescription = SelectedDevice.Description;
        CurrentSection = SectionDevices;
        StatusMessage = "Cihaz düzenleme modunda.";
    }

    private async Task DeleteSelectedDeviceAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        var device = SelectedDevice;
        if (!_dialogService.Confirm("Cihazı sil", $"{device.Name} ({device.IpAddress}) cihazını silmek istediğinize emin misiniz? Eski loglar korunur."))
        {
            return;
        }

        try
        {
            StatusMessage = "Cihaz siliniyor...";
            await _deviceRepository.DeleteAsync(device.Id);
            Devices.Remove(device);
            if (EditingDeviceId == device.Id)
            {
                ClearForm();
            }

            SelectedDevice = null;
            RefreshFilterOptions();
            StatusMessage = "Cihaz silindi.";
            UpdateSummaryCards();
        }
        catch (Exception ex)
        {
            StatusMessage = "Silme hatası";
            _dialogService.ShowError("Silme hatası", $"Cihaz silinemedi.\n\n{ex.Message}");
        }
    }

    private void ClearForm()
    {
        EditingDeviceId = null;
        FormName = string.Empty;
        FormIpAddress = string.Empty;
        FormDeviceType = DeviceType.Camera;
        FormLocation = string.Empty;
        FormGroupName = string.Empty;
        FormIsCritical = false;
        FormDescription = string.Empty;
        StatusMessage = "Form temizlendi.";
    }

    private Task PingByTypeAsync(DeviceType deviceType)
    {
        var targets = Devices.Where(device => device.DeviceType == deviceType).ToList();
        return PingDevicesAsync(targets, $"{deviceType.ToDisplayName()} cihazları kontrol ediliyor...");
    }

    private async Task PingDevicesAsync(IReadOnlyCollection<Device> targetDevices, string busyMessage, bool showFailureNotification = true)
    {
        if (IsPinging)
        {
            StatusMessage = "Devam eden ping turu bitmeden yeni tur başlatılamaz.";
            return;
        }

        var targets = targetDevices.DistinctBy(device => device.Id).ToList();
        if (targets.Count == 0)
        {
            _dialogService.ShowWarning("Cihaz bulunamadı", "Ping atılacak kayıtlı cihaz bulunamadı.");
            return;
        }

        var previousState = targets.ToDictionary(
            device => device.Id,
            device => (
                device.LastStatus,
                device.LastLatencyMs,
                device.LastCheckedAt,
                device.ConsecutiveFailures,
                device.ConsecutiveSuccesses,
                device.LastStableStatus));
        var targetLookup = targets.ToDictionary(device => device.Id);
        var options = new PingOptions(PingTimeoutMs, MaxParallelPings);

        try
        {
            IsBusy = true;
            IsPinging = true;
            _pingCancellationTokenSource = new CancellationTokenSource();
            ResetPingProgress(targets.Count);
            StatusMessage = busyMessage;

            foreach (var device in targets)
            {
                device.LastStatus = DeviceStatus.Checking;
            }

            var progress = new Progress<PingProgress>(value =>
            {
                PingTotalCount = value.Total;
                PingCompletedCount = value.Completed;
                PingSuccessCount = value.Success;
                PingFailureCount = value.Failure;

                if (value.DeviceId.HasValue && targetLookup.TryGetValue(value.DeviceId.Value, out var device) && value.DeviceStatus.HasValue)
                {
                    device.LastStatus = value.DeviceStatus.Value;
                    if (value.DeviceStatus != DeviceStatus.Checking)
                    {
                        device.LastLatencyMs = value.LatencyMs;
                        device.LastCheckedAt = value.CheckedAt;
                        device.UpdatedAt = DateTime.Now;
                        ApplyPingCounters(device, value.DeviceStatus.Value);
                    }
                }

                StatusMessage = $"Kontrol ediliyor... {value.Completed}/{value.Total} | Başarılı: {value.Success} | Başarısız: {value.Failure} | Kalan: {value.Remaining}";
            });

            var results = await _pingService.PingManyAsync(targets, options, progress, _pingCancellationTokenSource.Token);
            var wasCancelled = _pingCancellationTokenSource.IsCancellationRequested;

            RestoreUnfinishedDevices(targets, previousState);

            if (results.Count > 0)
            {
                await _deviceRepository.BulkUpdatePingResultsAsync(results);
                var logs = results.Select(CreateLog).ToList();
                await _pingLogRepository.AddRangeAsync(logs);
                InsertLogsAtTop(logs);
                await RefreshHealthMetricsAsync();
            }

            _failedLast24Hours = await _pingLogRepository.CountFailuresSinceAsync(DateTime.Now.AddHours(-24));
            UpdateSummaryCards();

            if (wasCancelled)
            {
                StatusMessage = $"Ping işlemi iptal edildi. Tamamlanan: {results.Count}/{targets.Count}.";
                return;
            }

            StatusMessage = $"Kontrol tamamlandı. Başarılı: {PingSuccessCount}, Başarısız: {PingFailureCount}.";
            if (showFailureNotification && PingFailureCount > 0)
            {
                _dialogService.ShowWarning("Kontrol tamamlandı", $"{PingFailureCount} cihaz erişilemiyor.");
            }
        }
        catch (Exception ex)
        {
            RestoreDevices(targets, previousState);
            StatusMessage = "Ping hatası";
            _dialogService.ShowError("Ping hatası", $"Ping işlemi tamamlanamadı.\n\n{ex.Message}");
        }
        finally
        {
            _pingCancellationTokenSource?.Dispose();
            _pingCancellationTokenSource = null;
            IsPinging = false;
            IsBusy = false;
            RaiseCommandStates();
        }
    }

    private void CancelPing()
    {
        if (!IsPinging)
        {
            return;
        }

        _pingCancellationTokenSource?.Cancel();
        StatusMessage = "Ping işlemi iptal ediliyor...";
    }

    private async Task LoadLogsAsync(bool silent = false)
    {
        try
        {
            if (!silent)
            {
                StatusMessage = "Loglar yükleniyor...";
            }

            var logs = await _pingLogRepository.GetFilteredAsync(
                LogStartDate,
                LogEndDate,
                LogDeviceNameFilter,
                LogIpAddressFilter,
                ParseDeviceTypeFilter(LogDeviceTypeFilter),
                ParseStatusFilter(LogStatusFilter),
                LogOnlyUnreachable);

            Logs.Clear();
            foreach (var log in logs)
            {
                Logs.Add(log);
            }

            _failedLast24Hours = await _pingLogRepository.CountFailuresSinceAsync(DateTime.Now.AddHours(-24));
            await RefreshHealthMetricsAsync();
            LogsView.Refresh();
            UpdateSummaryCards();

            if (!silent)
            {
                StatusMessage = "Loglar yenilendi.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = "Log yükleme hatası";
            _dialogService.ShowError("Log yükleme hatası", $"Loglar yüklenemedi.\n\n{ex.Message}");
        }
    }

    private async Task ShowSelectedDeviceLogsAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        LogDeviceNameFilter = string.Empty;
        LogIpAddressFilter = SelectedDevice.IpAddress;
        LogDeviceTypeFilter = AllDeviceTypesText;
        LogStatusFilter = AllStatusesText;
        LogOnlyUnreachable = false;
        CurrentSection = SectionLogs;
        await LoadLogsAsync();
    }

    private async Task ClearLogsAsync()
    {
        if (!_dialogService.Confirm("Logları temizle", "Tüm ping loglarını temizlemek istediğinize emin misiniz?"))
        {
            return;
        }

        try
        {
            StatusMessage = "Loglar temizleniyor...";
            await _pingLogRepository.ClearAsync();
            Logs.Clear();
            _failedLast24Hours = 0;
            UpdateSummaryCards();
            StatusMessage = "Loglar temizlendi.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Log temizleme hatası";
            _dialogService.ShowError("Log temizleme hatası", $"Loglar temizlenemedi.\n\n{ex.Message}");
        }
    }

    private async Task ClearOldLogsAsync()
    {
        if (LogRetentionDays <= 0)
        {
            _dialogService.ShowWarning("Log saklama", "Süresiz saklama seçiliyken eski log temizleme uygulanmaz.");
            return;
        }

        var threshold = DateTime.Now.AddDays(-LogRetentionDays);
        if (!_dialogService.Confirm("Eski logları temizle", $"{threshold:dd.MM.yyyy HH:mm} tarihinden eski loglar silinecek. Devam edilsin mi?"))
        {
            return;
        }

        try
        {
            StatusMessage = "Eski loglar temizleniyor...";
            var deleted = await _pingLogRepository.ClearOlderThanAsync(threshold);
            await LoadLogsAsync(silent: true);
            StatusMessage = $"{deleted} eski log kaydı temizlendi.";
            _dialogService.ShowInfo("Log temizleme", $"{deleted} log kaydı silindi.");
        }
        catch (Exception ex)
        {
            StatusMessage = "Log temizleme hatası";
            _dialogService.ShowError("Log temizleme hatası", $"Eski loglar temizlenemedi.\n\n{ex.Message}");
        }
    }

    private async Task ExportDevicesAsync()
    {
        var path = _dialogService.GetSaveCsvFilePath($"cihaz-listesi-{DateTime.Now:yyyyMMdd-HHmm}.csv");
        if (path is null)
        {
            return;
        }

        try
        {
            StatusMessage = "Cihaz listesi dışa aktarılıyor...";
            await _csvExportService.ExportDevicesAsync(DevicesView.Cast<Device>().ToList(), path, CsvDelimiter);
            StatusMessage = "Cihaz listesi dışa aktarıldı.";
            _dialogService.ShowInfo("Dışa aktarma tamamlandı", "Cihaz listesi CSV olarak kaydedildi.");
        }
        catch (Exception ex)
        {
            StatusMessage = "Dışa aktarma hatası";
            _dialogService.ShowError("Dışa aktarma hatası", $"Cihaz listesi dışa aktarılamadı.\n\n{ex.Message}");
        }
    }

    private async Task ImportDevicesAsync()
    {
        var path = _dialogService.GetOpenCsvFilePath();
        if (path is null)
        {
            return;
        }

        try
        {
            StatusMessage = "CSV dosyası okunuyor...";
            var preview = await _csvExportService.ReadDeviceImportPreviewAsync(path, Devices, CsvDelimiter);
            PendingImportPreview = preview;
            SelectedImportDuplicateAction = SkipExistingImportText;
            RefreshImportPreviewRows();
            CurrentSection = SectionDevices;
            StatusMessage = "CSV import ön izlemesi hazır.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Import hatası";
            _dialogService.ShowError("Import hatası", $"CSV import işlemi tamamlanamadı.\n\n{ex.Message}");
        }
    }

    private async Task ApplyImportPreviewAsync()
    {
        if (PendingImportPreview is null)
        {
            return;
        }

        var action = SelectedImportDuplicateAction == UpdateExistingImportText
            ? CsvImportDuplicateAction.UpdateExisting
            : CsvImportDuplicateAction.SkipExisting;

        try
        {
            StatusMessage = "Cihazlar import ediliyor...";
            var result = await _deviceRepository.ImportDevicesAsync(PendingImportPreview.ValidRows, action, PendingImportPreview.InvalidRowCount);
            PendingImportPreview = null;
            ImportPreviewRows.Clear();
            await ReloadDevicesAsync();
            await RefreshHealthMetricsAsync();
            UpdateSummaryCards();

            StatusMessage = "Import tamamlandı.";
            _dialogService.ShowInfo(
                "Import tamamlandı",
                $"Eklenen: {result.Added}\nGüncellenen: {result.Updated}\nAtlanan: {result.Skipped}\nHatalı satır: {result.Invalid}");
        }
        catch (Exception ex)
        {
            StatusMessage = "Import hatası";
            _dialogService.ShowError("Import hatası", $"CSV import işlemi tamamlanamadı.\n\n{ex.Message}");
        }
    }

    private void CancelImportPreview()
    {
        PendingImportPreview = null;
        ImportPreviewRows.Clear();
        StatusMessage = "Import ön izlemesi kapatıldı.";
    }

    private async Task ExportImportErrorsAsync()
    {
        if (PendingImportPreview is null || PendingImportPreview.InvalidRowCount == 0)
        {
            return;
        }

        var path = _dialogService.GetSaveCsvFilePath($"import-hatalari-{DateTime.Now:yyyyMMdd-HHmm}.csv");
        if (path is null)
        {
            return;
        }

        try
        {
            await _csvExportService.ExportImportErrorsAsync(PendingImportPreview.Errors, path, CsvDelimiter);
            StatusMessage = "Import hataları CSV olarak dışa aktarıldı.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Hata export işlemi başarısız.";
            _dialogService.ShowError("Hata export", $"Import hataları dışa aktarılamadı.\n\n{ex.Message}");
        }
    }

    private async Task CreateCsvTemplateAsync()
    {
        var path = _dialogService.GetSaveCsvFilePath("cihaz-import-sablonu.csv");
        if (path is null)
        {
            return;
        }

        try
        {
            await _csvExportService.ExportDeviceTemplateAsync(path, CsvDelimiter);
            StatusMessage = "CSV şablonu oluşturuldu.";
        }
        catch (Exception ex)
        {
            StatusMessage = "CSV şablonu oluşturulamadı.";
            _dialogService.ShowError("CSV şablonu", $"CSV şablonu oluşturulamadı.\n\n{ex.Message}");
        }
    }

    private async Task ExportSingleDeviceAsync(Device device)
    {
        var path = _dialogService.GetSaveCsvFilePath($"cihaz-{device.IpAddress}-{DateTime.Now:yyyyMMdd-HHmm}.csv");
        if (path is null)
        {
            return;
        }

        try
        {
            await _csvExportService.ExportDevicesAsync(new[] { device }, path, CsvDelimiter);
            StatusMessage = "Cihaz CSV olarak dışa aktarıldı.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Cihaz dışa aktarılamadı.";
            _dialogService.ShowError("Dışa aktarma", $"Cihaz dışa aktarılamadı.\n\n{ex.Message}");
        }
    }

    private async Task ExportLogsAsync()
    {
        var path = _dialogService.GetSaveCsvFilePath($"ping-loglari-{DateTime.Now:yyyyMMdd-HHmm}.csv");
        if (path is null)
        {
            return;
        }

        try
        {
            StatusMessage = "Loglar dışa aktarılıyor...";
            await _csvExportService.ExportLogsAsync(LogsView.Cast<PingLog>().ToList(), path, CsvDelimiter);
            StatusMessage = "Loglar dışa aktarıldı.";
            _dialogService.ShowInfo("Dışa aktarma tamamlandı", "Filtrelenmiş ping logları CSV olarak kaydedildi.");
        }
        catch (Exception ex)
        {
            StatusMessage = "Dışa aktarma hatası";
            _dialogService.ShowError("Dışa aktarma hatası", $"Loglar dışa aktarılamadı.\n\n{ex.Message}");
        }
    }

    private async Task SaveSettingsAsync()
    {
        if (PingTimeoutMs < 250 || PingTimeoutMs > 10000)
        {
            _dialogService.ShowWarning("Geçersiz ayar", "Ping timeout değeri 250 ile 10000 ms arasında olmalıdır.");
            return;
        }

        if (MaxParallelPings < 1 || MaxParallelPings > 128)
        {
            _dialogService.ShowWarning("Geçersiz ayar", "Maksimum paralel ping sayısı 1 ile 128 arasında olmalıdır.");
            return;
        }

        if (AutoCheckIntervalMinutes < 1)
        {
            _dialogService.ShowWarning("Geçersiz ayar", "Otomatik kontrol aralığı en az 1 dakika olmalıdır.");
            return;
        }

        if (string.IsNullOrWhiteSpace(CsvDelimiter))
        {
            _dialogService.ShowWarning("Geçersiz ayar", "CSV ayırıcısı boş olamaz.");
            return;
        }

        if (LogRetentionDays < 0)
        {
            _dialogService.ShowWarning("Geçersiz ayar", "Log saklama günü 0 veya daha büyük olmalıdır. 0 süresiz anlamına gelir.");
            return;
        }

        try
        {
            var settings = new AppSettings
            {
                PingTimeoutMs = PingTimeoutMs,
                MaxParallelPings = MaxParallelPings,
                AutoCheckEnabled = AutoCheckEnabled,
                AutoCheckIntervalMinutes = AutoCheckIntervalMinutes,
                CsvDelimiter = CsvDelimiter[..1],
                LogRetentionDays = LogRetentionDays
            };

            await _settingsService.SaveAsync(settings);
            ApplySettings(settings);
            UpdateAutoCheckTimer();
            StatusMessage = "Ayarlar kaydedildi.";
            _dialogService.ShowInfo("Ayarlar", "Ayarlar kaydedildi ve hemen geçerli oldu.");
        }
        catch (Exception ex)
        {
            StatusMessage = "Ayar kayıt hatası";
            _dialogService.ShowError("Ayar kayıt hatası", $"Ayarlar kaydedilemedi.\n\n{ex.Message}");
        }
    }

    private async Task BackupDatabaseAsync()
    {
        var path = _dialogService.GetSaveDatabaseFilePath($"network-health-monitor-{DateTime.Now:yyyyMMdd-HHmm}.db");
        if (path is null)
        {
            return;
        }

        try
        {
            StatusMessage = "Veritabanı yedekleniyor...";
            await _maintenanceService.BackupDatabaseAsync(path);
            StatusMessage = "Veritabanı yedeklendi.";
            _dialogService.ShowInfo("Veritabanı yedeği", "Veritabanı seçilen konuma yedeklendi.");
        }
        catch (Exception ex)
        {
            StatusMessage = "Veritabanı yedeklenemedi.";
            _dialogService.ShowError("Veritabanı yedeği", $"Veritabanı yedeklenemedi.\n\n{ex.Message}");
        }
    }

    private async Task RestoreDatabaseAsync()
    {
        var path = _dialogService.GetOpenDatabaseFilePath();
        if (path is null)
        {
            return;
        }

        if (!_dialogService.Confirm("Veritabanını geri yükle", "Mevcut veritabanı otomatik yedeklenecek ve seçilen dosya geri yüklenecek. Devam edilsin mi?"))
        {
            return;
        }

        try
        {
            StatusMessage = "Veritabanı geri yükleniyor...";
            var automaticBackupPath = await _maintenanceService.RestoreDatabaseAsync(path);
            await ReloadDevicesAsync();
            await LoadLogsAsync(silent: true);
            await RefreshHealthMetricsAsync();
            StatusMessage = "Veritabanı geri yüklendi.";
            _dialogService.ShowInfo("Veritabanı geri yükleme", $"Geri yükleme tamamlandı.\nOtomatik yedek: {automaticBackupPath}");
        }
        catch (Exception ex)
        {
            StatusMessage = "Veritabanı geri yüklenemedi.";
            _dialogService.ShowError("Veritabanı geri yükleme", $"Veritabanı geri yüklenemedi.\n\n{ex.Message}");
        }
    }

    private async Task ExportSettingsAsync()
    {
        var path = _dialogService.GetSaveJsonFilePath($"network-health-monitor-settings-{DateTime.Now:yyyyMMdd-HHmm}.json");
        if (path is null)
        {
            return;
        }

        try
        {
            await _settingsService.SaveAsync(new AppSettings
            {
                PingTimeoutMs = PingTimeoutMs,
                MaxParallelPings = MaxParallelPings,
                AutoCheckEnabled = AutoCheckEnabled,
                AutoCheckIntervalMinutes = AutoCheckIntervalMinutes,
                CsvDelimiter = CsvDelimiter,
                LogRetentionDays = LogRetentionDays
            });
            await _maintenanceService.ExportSettingsAsync(path);
            StatusMessage = "Ayarlar dışa aktarıldı.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Ayarlar dışa aktarılamadı.";
            _dialogService.ShowError("Ayar export", $"Ayarlar dışa aktarılamadı.\n\n{ex.Message}");
        }
    }

    private async Task ImportSettingsAsync()
    {
        var path = _dialogService.GetOpenJsonFilePath();
        if (path is null)
        {
            return;
        }

        if (!_dialogService.Confirm("Ayarları içe aktar", "Seçilen ayar dosyası mevcut ayarların üzerine yazılacak. Devam edilsin mi?"))
        {
            return;
        }

        try
        {
            await _maintenanceService.ImportSettingsAsync(path);
            var settings = await _settingsService.LoadAsync();
            ApplySettings(settings);
            UpdateAutoCheckTimer();
            StatusMessage = "Ayarlar içe aktarıldı.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Ayarlar içe aktarılamadı.";
            _dialogService.ShowError("Ayar import", $"Ayarlar içe aktarılamadı.\n\n{ex.Message}");
        }
    }

    private void AutoCheckTimerTick(object? sender, EventArgs e)
    {
        _ = RunAutoCheckAsync();
    }

    private async Task RunAutoCheckAsync()
    {
        if (IsPinging || IsBusy || !AutoCheckEnabled || Devices.Count == 0)
        {
            return;
        }

        await PingDevicesAsync(Devices.ToList(), "Otomatik kontrol çalışıyor...", showFailureNotification: false);
    }

    private void UpdateAutoCheckTimer()
    {
        _autoCheckTimer.Stop();
        if (!AutoCheckEnabled)
        {
            return;
        }

        _autoCheckTimer.Interval = TimeSpan.FromMinutes(Math.Max(1, AutoCheckIntervalMinutes));
        _autoCheckTimer.Start();
    }

    private bool FilterDevice(object item)
    {
        if (item is not Device device)
        {
            return false;
        }

        if (ParseDeviceTypeFilter(DeviceTypeFilter) is { } deviceType && device.DeviceType != deviceType)
        {
            return false;
        }

        if (ParseStatusFilter(DeviceStatusFilter) is { } status && device.LastStatus != status)
        {
            return false;
        }

        if (LocationFilter != AllLocationsText && !string.Equals(device.Location, LocationFilter, StringComparison.CurrentCultureIgnoreCase))
        {
            return false;
        }

        if (GroupFilter != AllGroupsText && !string.Equals(device.GroupName, GroupFilter, StringComparison.CurrentCultureIgnoreCase))
        {
            return false;
        }

        if (CriticalFilter == CriticalOnlyText && !device.IsCritical)
        {
            return false;
        }

        if (CriticalFilter == NonCriticalOnlyText && device.IsCritical)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(DeviceSearchText))
        {
            return true;
        }

        var searchText = DeviceSearchText.Trim();
        return Contains(device.Name, searchText)
               || Contains(device.IpAddress, searchText)
               || Contains(device.Description, searchText)
               || Contains(device.Location, searchText)
               || Contains(device.GroupName, searchText);
    }

    private bool FilterLog(object item)
    {
        if (item is not PingLog log)
        {
            return false;
        }

        if (LogStartDate.HasValue && log.CheckedAt < LogStartDate.Value.Date)
        {
            return false;
        }

        if (LogEndDate.HasValue && log.CheckedAt >= LogEndDate.Value.Date.AddDays(1))
        {
            return false;
        }

        if (ParseDeviceTypeFilter(LogDeviceTypeFilter) is { } deviceType && log.DeviceType != deviceType)
        {
            return false;
        }

        if (LogOnlyUnreachable && log.Status != DeviceStatus.Unreachable)
        {
            return false;
        }

        if (!LogOnlyUnreachable && ParseStatusFilter(LogStatusFilter) is { } status && log.Status != status)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(LogDeviceNameFilter) && !Contains(log.DeviceName, LogDeviceNameFilter.Trim()))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(LogIpAddressFilter) && !Contains(log.IpAddress, LogIpAddressFilter.Trim()))
        {
            return false;
        }

        return true;
    }

    private static bool Contains(string? source, string value)
    {
        return source?.Contains(value, StringComparison.CurrentCultureIgnoreCase) == true;
    }

    private void DevicesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (Device device in e.NewItems)
            {
                device.PropertyChanged += DevicePropertyChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (Device device in e.OldItems)
            {
                device.PropertyChanged -= DevicePropertyChanged;
            }
        }

        if (!_suppressDeviceViewRefresh)
        {
            RefreshFilterOptions();
            DevicesView.Refresh();
        }

        UpdateSummaryCards();
        RaiseCommandStates();
    }

    private void DevicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if ((e.PropertyName is nameof(Device.Name)
                or nameof(Device.IpAddress)
                or nameof(Device.DeviceType)
                or nameof(Device.Location)
                or nameof(Device.GroupName)
                or nameof(Device.IsCritical)
                or nameof(Device.Description))
            || (e.PropertyName == nameof(Device.LastStatus) && DeviceStatusFilter != AllStatusesText))
        {
            DevicesView.Refresh();
        }

        UpdateSummaryCards();
        RaiseCommandStates();
    }

    private void AddDeviceToCollection(Device device)
    {
        Devices.Add(device);
    }

    private async Task ReloadDevicesAsync()
    {
        var devices = await _deviceRepository.GetAllAsync();
        _suppressDeviceViewRefresh = true;
        Devices.Clear();
        foreach (var device in devices)
        {
            AddDeviceToCollection(device);
        }

        _suppressDeviceViewRefresh = false;
        RefreshFilterOptions();
        DevicesView.Refresh();
        RaiseCommandStates();
    }

    private static void ApplyDeviceUpdate(Device target, Device source)
    {
        target.Name = source.Name;
        target.IpAddress = source.IpAddress;
        target.DeviceType = source.DeviceType;
        target.Location = source.Location;
        target.GroupName = source.GroupName;
        target.IsCritical = source.IsCritical;
        target.Description = source.Description;
        target.LastStatus = source.LastStatus;
        target.LastLatencyMs = source.LastLatencyMs;
        target.LastCheckedAt = source.LastCheckedAt;
        target.ConsecutiveFailures = source.ConsecutiveFailures;
        target.ConsecutiveSuccesses = source.ConsecutiveSuccesses;
        target.LastStableStatus = source.LastStableStatus;
        target.CreatedAt = source.CreatedAt;
        target.UpdatedAt = source.UpdatedAt;
    }

    private void EnsureSummaryCards()
    {
        if (SummaryCards.Count == 0)
        {
            SummaryCards.Add(new SummaryCardViewModel("Toplam cihaz", "0", "#2563EB"));
            SummaryCards.Add(new SummaryCardViewModel("Erişilebilir", "0", "#16A34A"));
            SummaryCards.Add(new SummaryCardViewModel("Erişilemiyor", "0", "#DC2626"));
            SummaryCards.Add(new SummaryCardViewModel("Kontrol edilmedi", "0", "#64748B"));
            SummaryCards.Add(new SummaryCardViewModel("Ort. gecikme", "-", "#0F766E"));
            SummaryCards.Add(new SummaryCardViewModel("Son kontrol", "-", "#475569"));
            SummaryCards.Add(new SummaryCardViewModel("Kritik sorun", "0", "#B91C1C"));
            SummaryCards.Add(new SummaryCardViewModel("Genel uptime", "-", "#2563EB"));
        }

        if (DeviceTypeDistribution.Count == 0)
        {
            DeviceTypeDistribution.Add(new SummaryCardViewModel("Kamera", "0", "#0891B2"));
            DeviceTypeDistribution.Add(new SummaryCardViewModel("Access Point", "0", "#7C3AED"));
            DeviceTypeDistribution.Add(new SummaryCardViewModel("Bilgisayar", "0", "#EA580C"));
            DeviceTypeDistribution.Add(new SummaryCardViewModel("Switch", "0", "#0F766E"));
            DeviceTypeDistribution.Add(new SummaryCardViewModel("Diğer", "0", "#4B5563"));
        }
    }

    private void UpdateSummaryCards()
    {
        EnsureSummaryCards();

        var reachableDevices = Devices.Where(device => device.LastStatus == DeviceStatus.Reachable).ToList();
        var lastChecked = Devices
            .Where(device => device.LastCheckedAt.HasValue)
            .Select(device => device.LastCheckedAt!.Value)
            .OrderByDescending(value => value)
            .FirstOrDefault();

        SummaryCards[0].Value = Devices.Count.ToString(CultureInfo.CurrentCulture);
        SummaryCards[1].Value = reachableDevices.Count.ToString(CultureInfo.CurrentCulture);
        SummaryCards[2].Value = Devices.Count(device => device.LastStatus == DeviceStatus.Unreachable).ToString(CultureInfo.CurrentCulture);
        SummaryCards[3].Value = Devices.Count(device => device.LastStatus == DeviceStatus.NotChecked).ToString(CultureInfo.CurrentCulture);
        SummaryCards[4].Value = reachableDevices.Count == 0
            ? "-"
            : $"{reachableDevices.Where(device => device.LastLatencyMs.HasValue).DefaultIfEmpty().Average(device => device?.LastLatencyMs ?? 0):0} ms";
        SummaryCards[5].Value = lastChecked == default ? "-" : lastChecked.ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture);
        SummaryCards[6].Value = Devices.Count(device => device.IsCritical && device.LastStatus == DeviceStatus.Unreachable).ToString(CultureInfo.CurrentCulture);
        SummaryCards[7].Value = OverallUptimeText;

        DeviceTypeDistribution[0].Value = Devices.Count(device => device.DeviceType == DeviceType.Camera).ToString(CultureInfo.CurrentCulture);
        DeviceTypeDistribution[1].Value = Devices.Count(device => device.DeviceType == DeviceType.AccessPoint).ToString(CultureInfo.CurrentCulture);
        DeviceTypeDistribution[2].Value = Devices.Count(device => device.DeviceType == DeviceType.Computer).ToString(CultureInfo.CurrentCulture);
        DeviceTypeDistribution[3].Value = Devices.Count(device => device.DeviceType == DeviceType.Switch).ToString(CultureInfo.CurrentCulture);
        DeviceTypeDistribution[4].Value = Devices.Count(device => device.DeviceType == DeviceType.Other).ToString(CultureInfo.CurrentCulture);

        ReplaceCollection(
            RecentUnreachableDevices,
            Devices.Where(device => device.LastStatus == DeviceStatus.Unreachable)
                .OrderByDescending(device => device.LastCheckedAt ?? DateTime.MinValue)
                .Take(10));

        ReplaceCollection(
            HighLatencyDevices,
            Devices.Where(device => device.LastLatencyMs.HasValue)
                .OrderByDescending(device => device.LastLatencyMs)
                .Take(10));

        ReplaceCollection(
            CriticalProblemDevices,
            Devices.Where(device => device.IsCritical && device.LastStatus == DeviceStatus.Unreachable)
                .OrderByDescending(device => device.ConsecutiveFailures)
                .ThenByDescending(device => device.LastCheckedAt ?? DateTime.MinValue)
                .Take(20));

        ReplaceCollection(
            MostProblematicDevices,
            Devices.Where(device => device.Uptime30DaysPercent.HasValue)
                .OrderBy(device => device.Uptime30DaysPercent)
                .ThenByDescending(device => device.ConsecutiveFailures)
                .Take(10));

        OnPropertyChanged(nameof(HasCriticalProblems));
        OnPropertyChanged(nameof(CriticalWarningText));
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> collection, IEnumerable<T> values)
    {
        collection.Clear();
        foreach (var value in values)
        {
            collection.Add(value);
        }
    }

    private async Task RefreshHealthMetricsAsync()
    {
        var metrics = await _pingLogRepository.GetDeviceHealthMetricsAsync(DateTime.Now.AddDays(-30));
        foreach (var device in Devices)
        {
            if (metrics.TryGetValue(device.Id, out var metric))
            {
                device.Uptime24HoursPercent = metric.Uptime24HoursPercent;
                device.Uptime7DaysPercent = metric.Uptime7DaysPercent;
                device.Uptime30DaysPercent = metric.Uptime30DaysPercent;
                device.AverageLatencyMs = metric.AverageLatencyMs;
                device.LastFailureAt = metric.LastFailureAt;
            }
            else
            {
                device.Uptime24HoursPercent = null;
                device.Uptime7DaysPercent = null;
                device.Uptime30DaysPercent = null;
                device.AverageLatencyMs = null;
                device.LastFailureAt = null;
            }
        }

        var allKnown = Devices.Where(device => device.Uptime24HoursPercent.HasValue).ToList();
        OverallUptimeText = allKnown.Count == 0 ? "-" : $"{allKnown.Average(device => device.Uptime24HoursPercent!.Value):0.0}%";

        var criticalKnown = Devices.Where(device => device.IsCritical && device.Uptime24HoursPercent.HasValue).ToList();
        CriticalUptimeText = criticalKnown.Count == 0 ? "-" : $"{criticalKnown.Average(device => device.Uptime24HoursPercent!.Value):0.0}%";
    }

    private void RefreshFilterOptions()
    {
        ReplaceOptions(LocationFilterOptions, AllLocationsText, Devices.Select(device => device.Location));
        ReplaceOptions(GroupFilterOptions, AllGroupsText, Devices.Select(device => device.GroupName));

        if (!LocationFilterOptions.Contains(LocationFilter))
        {
            LocationFilter = AllLocationsText;
        }

        if (!GroupFilterOptions.Contains(GroupFilter))
        {
            GroupFilter = AllGroupsText;
        }
    }

    private static void ReplaceOptions(ObservableCollection<string> target, string allText, IEnumerable<string> values)
    {
        target.Clear();
        target.Add(allText);

        foreach (var value in values
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.CurrentCultureIgnoreCase)
                     .OrderBy(value => value))
        {
            target.Add(value);
        }
    }

    private void RefreshImportPreviewRows()
    {
        ImportPreviewRows.Clear();
        if (PendingImportPreview is null)
        {
            RaiseCommandStates();
            return;
        }

        var duplicateAction = SelectedImportDuplicateAction == UpdateExistingImportText
            ? CsvImportRowStatus.Update
            : CsvImportRowStatus.Skip;

        foreach (var row in PendingImportPreview.Rows)
        {
            if (row.ExistsInDatabase && row.Status != CsvImportRowStatus.Invalid)
            {
                row.Status = duplicateAction;
            }
            else if (!row.ExistsInDatabase && row.Status != CsvImportRowStatus.Invalid)
            {
                row.Status = CsvImportRowStatus.Add;
            }

            ImportPreviewRows.Add(row);
        }

        OnPropertyChanged(nameof(ImportPreviewSummaryText));
        RaiseCommandStates();
    }

    private static void ApplyPingCounters(Device device, DeviceStatus status)
    {
        if (status == DeviceStatus.Reachable)
        {
            device.ConsecutiveSuccesses++;
            device.ConsecutiveFailures = 0;
            device.LastStableStatus = DeviceStatus.Reachable;
            return;
        }

        if (status == DeviceStatus.Unreachable)
        {
            device.ConsecutiveFailures++;
            device.ConsecutiveSuccesses = 0;
            if (device.ConsecutiveFailures >= 3)
            {
                device.LastStableStatus = DeviceStatus.Unreachable;
            }
        }
    }

    private void ResetPingProgress(int total)
    {
        PingTotalCount = total;
        PingCompletedCount = 0;
        PingSuccessCount = 0;
        PingFailureCount = 0;
    }

    private static PingLog CreateLog(PingDeviceResult result)
    {
        return new PingLog
        {
            DeviceId = result.Device.Id,
            DeviceName = result.Device.Name,
            IpAddress = result.Device.IpAddress,
            DeviceType = result.Device.DeviceType,
            Status = result.Status,
            LatencyMs = result.LatencyMs,
            ResponseMessage = result.ResponseMessage,
            ErrorMessage = result.ErrorMessage,
            CheckedAt = result.CheckedAt
        };
    }

    private void InsertLogsAtTop(IReadOnlyCollection<PingLog> logs)
    {
        foreach (var log in logs.OrderBy(log => log.CheckedAt))
        {
            Logs.Insert(0, log);
        }

        while (Logs.Count > 5000)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }
    }

    private static void RestoreUnfinishedDevices(
        IEnumerable<Device> targets,
        IReadOnlyDictionary<int, (DeviceStatus LastStatus, long? LastLatencyMs, DateTime? LastCheckedAt, int ConsecutiveFailures, int ConsecutiveSuccesses, DeviceStatus LastStableStatus)> previousState)
    {
        foreach (var device in targets.Where(device => device.LastStatus == DeviceStatus.Checking))
        {
            if (!previousState.TryGetValue(device.Id, out var state))
            {
                continue;
            }

            device.LastStatus = state.LastStatus;
            device.LastLatencyMs = state.LastLatencyMs;
            device.LastCheckedAt = state.LastCheckedAt;
            device.ConsecutiveFailures = state.ConsecutiveFailures;
            device.ConsecutiveSuccesses = state.ConsecutiveSuccesses;
            device.LastStableStatus = state.LastStableStatus;
        }
    }

    private static void RestoreDevices(
        IEnumerable<Device> targets,
        IReadOnlyDictionary<int, (DeviceStatus LastStatus, long? LastLatencyMs, DateTime? LastCheckedAt, int ConsecutiveFailures, int ConsecutiveSuccesses, DeviceStatus LastStableStatus)> previousState)
    {
        foreach (var device in targets)
        {
            if (!previousState.TryGetValue(device.Id, out var state))
            {
                continue;
            }

            device.LastStatus = state.LastStatus;
            device.LastLatencyMs = state.LastLatencyMs;
            device.LastCheckedAt = state.LastCheckedAt;
            device.ConsecutiveFailures = state.ConsecutiveFailures;
            device.ConsecutiveSuccesses = state.ConsecutiveSuccesses;
            device.LastStableStatus = state.LastStableStatus;
        }
    }

    private string BuildImportPreviewMessage(CsvImportPreview preview)
    {
        var lines = new List<string>
        {
            $"Toplam satır: {preview.TotalRows}",
            $"Eklenecek cihaz: {preview.AddCount}",
            $"Veritabanında aynı IP: {preview.ExistingIpCount}",
            $"Hatalı satır: {preview.InvalidRowCount}"
        };

        if (preview.Errors.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("İlk hatalar:");
            lines.AddRange(preview.Errors.Take(8).Select(error => $"{error.RowNumber}. satır: {error.Error} ({error.IpAddress})"));
            if (preview.Errors.Count > 8)
            {
                lines.Add($"... {preview.Errors.Count - 8} hata daha");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void ApplySettings(AppSettings settings)
    {
        PingTimeoutMs = Math.Clamp(settings.PingTimeoutMs, 250, 10000);
        MaxParallelPings = Math.Clamp(settings.MaxParallelPings, 1, 128);
        AutoCheckEnabled = settings.AutoCheckEnabled;
        AutoCheckIntervalMinutes = Math.Max(1, settings.AutoCheckIntervalMinutes);
        CsvDelimiter = string.IsNullOrWhiteSpace(settings.CsvDelimiter) ? ";" : settings.CsvDelimiter[..1];
        LogRetentionDays = Math.Max(0, settings.LogRetentionDays);
    }

    private static DeviceType? ParseDeviceTypeFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == AllDeviceTypesText)
        {
            return null;
        }

        return DeviceTypeExtensions.TryParse(value, out var parsed) ? parsed : null;
    }

    private static DeviceStatus? ParseStatusFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == AllStatusesText)
        {
            return null;
        }

        foreach (var status in Enum.GetValues<DeviceStatus>())
        {
            if (string.Equals(status.ToDisplayName(), value, StringComparison.CurrentCultureIgnoreCase))
            {
                return status;
            }
        }

        return null;
    }

    private void RaiseCommandStates()
    {
        SaveDeviceCommand?.NotifyCanExecuteChanged();
        EditSelectedDeviceCommand?.NotifyCanExecuteChanged();
        DeleteSelectedDeviceCommand?.NotifyCanExecuteChanged();
        ClearFormCommand?.NotifyCanExecuteChanged();
        PingAllCommand?.NotifyCanExecuteChanged();
        PingFilteredDevicesCommand?.NotifyCanExecuteChanged();
        PingCamerasCommand?.NotifyCanExecuteChanged();
        PingAccessPointsCommand?.NotifyCanExecuteChanged();
        PingComputersCommand?.NotifyCanExecuteChanged();
        PingSwitchesCommand?.NotifyCanExecuteChanged();
        PingOthersCommand?.NotifyCanExecuteChanged();
        PingSelectedDeviceCommand?.NotifyCanExecuteChanged();
        PingDeviceCommand?.NotifyCanExecuteChanged();
        EditDeviceCommand?.NotifyCanExecuteChanged();
        DeleteDeviceCommand?.NotifyCanExecuteChanged();
        CancelPingCommand?.NotifyCanExecuteChanged();
        RefreshLogsCommand?.NotifyCanExecuteChanged();
        ShowSelectedDeviceLogsCommand?.NotifyCanExecuteChanged();
        ShowDeviceLogsCommand?.NotifyCanExecuteChanged();
        ClearLogsCommand?.NotifyCanExecuteChanged();
        ClearOldLogsCommand?.NotifyCanExecuteChanged();
        ExportDevicesCommand?.NotifyCanExecuteChanged();
        ImportDevicesCommand?.NotifyCanExecuteChanged();
        ApplyImportPreviewCommand?.NotifyCanExecuteChanged();
        CancelImportPreviewCommand?.NotifyCanExecuteChanged();
        ExportImportErrorsCommand?.NotifyCanExecuteChanged();
        CreateCsvTemplateCommand?.NotifyCanExecuteChanged();
        CopyDeviceIpCommand?.NotifyCanExecuteChanged();
        ExportSingleDeviceCommand?.NotifyCanExecuteChanged();
        ExportLogsCommand?.NotifyCanExecuteChanged();
        SaveSettingsCommand?.NotifyCanExecuteChanged();
        BackupDatabaseCommand?.NotifyCanExecuteChanged();
        RestoreDatabaseCommand?.NotifyCanExecuteChanged();
        ExportSettingsCommand?.NotifyCanExecuteChanged();
        ImportSettingsCommand?.NotifyCanExecuteChanged();
    }
}
