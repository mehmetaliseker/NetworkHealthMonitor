using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Infrastructure;
using NetworkHealthMonitor.Models;
using NetworkHealthMonitor.Services;

namespace NetworkHealthMonitor.ViewModels;

public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private const string SectionDashboard = "Dashboard";
    private const string SectionDevices = "Cihazlar";
    private const string SectionDeviceEdit = "Cihaz Ekle / Düzenle";
    private const string SectionGroups = "Cihaz Grupları";
    private const string SectionSchedules = "Otomatik Kontrol Planları";
    private const string SectionAvailability = "Uptime / Erişilebilirlik";
    private const string SectionLogs = "Ping Logları";
    private const string SectionSettings = "Ayarlar";
    private const string AllDeviceTypesText = "Tüm Tipler";
    private const string AllStatusesText = "Tüm Durumlar";
    private const string AllGroupsText = "Tüm Gruplar";
    private const string AllTriggersText = "Tüm Kaynaklar";
    private const string AllCriticalText = "Tüm Cihazlar";
    private const string CriticalOnlyText = "Sadece kritik";
    private const string NonCriticalOnlyText = "Kritik olmayan";

    private readonly DeviceRepository _deviceRepository;
    private readonly DeviceGroupRepository _deviceGroupRepository;
    private readonly PingLogRepository _pingLogRepository;
    private readonly SchedulePlanRepository _schedulePlanRepository;
    private readonly OutageRepository _outageRepository;
    private readonly IDeviceService _deviceService;
    private readonly IDeviceGroupService _deviceGroupService;
    private readonly ISchedulePlanService _schedulePlanService;
    private readonly IPingExecutionService _pingExecutionService;
    private readonly IAvailabilityService _availabilityService;
    private readonly ISchedulerService _schedulerService;
    private readonly CsvExportService _csvExportService;
    private readonly AppSettingsService _settingsService;
    private readonly IDialogService _dialogService;
    private readonly DataMaintenanceService _maintenanceService;

    private CancellationTokenSource? _pingCancellationTokenSource;
    private bool _isInitialized;
    private bool _isBusy;
    private bool _isPinging;
    private string _currentSection = SectionDashboard;
    private string _statusMessage = "Başlatılıyor...";
    private Device? _selectedDevice;
    private DeviceGroup? _selectedGroup;
    private SchedulePlan? _selectedSchedulePlan;
    private DateTime? _logStartDate;
    private DateTime? _logEndDate;
    private string _deviceSearchText = string.Empty;
    private string _deviceTypeFilter = AllDeviceTypesText;
    private string _deviceStatusFilter = AllStatusesText;
    private string _deviceGroupFilter = AllGroupsText;
    private string _criticalFilter = AllCriticalText;
    private string _logDeviceNameFilter = string.Empty;
    private string _logIpAddressFilter = string.Empty;
    private string _logDeviceTypeFilter = AllDeviceTypesText;
    private string _logStatusFilter = AllStatusesText;
    private string _logGroupFilter = AllGroupsText;
    private string _logTriggerFilter = AllTriggersText;
    private string _logPlanNameFilter = string.Empty;
    private bool _logOnlyUnreachable;
    private int? _editingDeviceId;
    private string _formName = string.Empty;
    private string _formIpAddress = string.Empty;
    private DeviceType _formDeviceType = DeviceType.Camera;
    private int? _formGroupId;
    private string _formLocation = string.Empty;
    private string _formDescription = string.Empty;
    private bool _formAutoCheckEnabled = true;
    private int? _formDefaultSchedulePlanId;
    private bool _formIsCritical;
    private bool _formIsActive = true;
    private int? _editingGroupId;
    private string _groupFormName = string.Empty;
    private string _groupFormDescription = string.Empty;
    private int? _groupFormDefaultSchedulePlanId;
    private int? _editingPlanId;
    private string _planFormName = string.Empty;
    private SchedulePlanTargetType _planFormTargetType = SchedulePlanTargetType.AllDevices;
    private string _planFormTargetValue = string.Empty;
    private int _planFormFrequencyValue = 10;
    private string _planFormFrequencyUnit = "Dakika";
    private int _planFormTimeoutMs = 1000;
    private int _planFormMaxParallelism = 16;
    private int _planFormFailureThreshold = 3;
    private bool _planFormIsActive = true;
    private string _planFormDescription = string.Empty;
    private int _pingTimeoutMs = 1000;
    private int _maxParallelPings = 32;
    private int _defaultFailureThreshold = 3;
    private bool _startSchedulePlansOnStartup = true;
    private string _csvDelimiter = ";";
    private int _logRetentionDays = 30;
    private string _theme = "Açık";
    private int _pingTotalCount;
    private int _pingCompletedCount;
    private int _pingSuccessCount;
    private int _pingFailureCount;
    private bool _isSchedulerRunning;

    public MainViewModel(
        DeviceRepository deviceRepository,
        DeviceGroupRepository deviceGroupRepository,
        PingLogRepository pingLogRepository,
        SchedulePlanRepository schedulePlanRepository,
        OutageRepository outageRepository,
        IDeviceService deviceService,
        IDeviceGroupService deviceGroupService,
        ISchedulePlanService schedulePlanService,
        IPingExecutionService pingExecutionService,
        IAvailabilityService availabilityService,
        ISchedulerService schedulerService,
        CsvExportService csvExportService,
        AppSettingsService settingsService,
        IDialogService dialogService,
        DataMaintenanceService maintenanceService)
    {
        _deviceRepository = deviceRepository;
        _deviceGroupRepository = deviceGroupRepository;
        _pingLogRepository = pingLogRepository;
        _schedulePlanRepository = schedulePlanRepository;
        _outageRepository = outageRepository;
        _deviceService = deviceService;
        _deviceGroupService = deviceGroupService;
        _schedulePlanService = schedulePlanService;
        _pingExecutionService = pingExecutionService;
        _availabilityService = availabilityService;
        _schedulerService = schedulerService;
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
            DeviceStatus.NotChecked.ToDisplayName()
        });
        CriticalFilterOptions = new ObservableCollection<string>(new[] { AllCriticalText, CriticalOnlyText, NonCriticalOnlyText });
        TriggerFilterOptions = new ObservableCollection<string>(new[]
        {
            AllTriggersText,
            PingTriggerType.Manual.ToDisplayName(),
            PingTriggerType.SelectedDeviceManual.ToDisplayName(),
            PingTriggerType.GroupManual.ToDisplayName(),
            PingTriggerType.TypeManual.ToDisplayName(),
            PingTriggerType.Scheduled.ToDisplayName()
        });
        PlanTargetTypeOptions = new ObservableCollection<SelectionOption<SchedulePlanTargetType>>(
            Enum.GetValues<SchedulePlanTargetType>().Select(type => new SelectionOption<SchedulePlanTargetType>(type, type.ToDisplayName())));
        FrequencyUnitOptions = new ObservableCollection<string>(new[] { "Dakika", "Saat", "Gün" });
        ThemeOptions = new ObservableCollection<string>(new[] { "Açık", "Koyu" });

        DevicesView = CollectionViewSource.GetDefaultView(Devices);
        DevicesView.Filter = FilterDevice;
        DevicesView.SortDescriptions.Add(new SortDescription(nameof(Device.Name), ListSortDirection.Ascending));
        DevicesView.SortDescriptions.Add(new SortDescription(nameof(Device.IpSortKey), ListSortDirection.Ascending));

        LogsView = CollectionViewSource.GetDefaultView(Logs);
        LogsView.SortDescriptions.Add(new SortDescription(nameof(PingLog.CheckedAt), ListSortDirection.Descending));

        NavigateDashboardCommand = new RelayCommand(() => CurrentSection = SectionDashboard);
        NavigateDevicesCommand = new RelayCommand(() => CurrentSection = SectionDevices);
        NavigateDeviceEditCommand = new RelayCommand(() => CurrentSection = SectionDeviceEdit);
        NavigateGroupsCommand = new RelayCommand(() => CurrentSection = SectionGroups);
        NavigateSchedulesCommand = new RelayCommand(() => CurrentSection = SectionSchedules);
        NavigateAvailabilityCommand = new RelayCommand(() => CurrentSection = SectionAvailability);
        NavigateLogsCommand = new RelayCommand(() => CurrentSection = SectionLogs);
        NavigateSettingsCommand = new RelayCommand(() => CurrentSection = SectionSettings);

        SaveDeviceCommand = new AsyncRelayCommand(SaveDeviceAsync, () => !IsBusy);
        ClearDeviceFormCommand = new RelayCommand(ClearDeviceForm, () => !IsBusy);
        EditSelectedDeviceCommand = new RelayCommand(() => StartEditDevice(SelectedDevice), () => SelectedDevice is not null && !IsBusy);
        EditDeviceCommand = new RelayCommand<Device>(StartEditDevice, device => device is not null && !IsBusy);
        DeleteSelectedDeviceCommand = new AsyncRelayCommand(() => DeleteDeviceAsync(SelectedDevice), () => SelectedDevice is not null && !IsBusy);
        DeleteDeviceCommand = new AsyncRelayCommand<Device>(DeleteDeviceAsync, device => device is not null && !IsBusy);

        PingAllCommand = new AsyncRelayCommand(() => RunManualPingAsync(Devices.ToList(), PingTriggerType.Manual), () => Devices.Count > 0 && !IsBusy);
        PingFilteredDevicesCommand = new AsyncRelayCommand(() => RunManualPingAsync(DevicesView.Cast<Device>().ToList(), PingTriggerType.Manual), () => DevicesView.Cast<Device>().Any() && !IsBusy);
        PingSelectedDeviceCommand = new AsyncRelayCommand(() => SelectedDevice is null ? Task.CompletedTask : RunManualPingAsync(new[] { SelectedDevice }, PingTriggerType.SelectedDeviceManual), () => SelectedDevice is not null && !IsBusy);
        PingDeviceCommand = new AsyncRelayCommand<Device>(device => device is null ? Task.CompletedTask : RunManualPingAsync(new[] { device }, PingTriggerType.SelectedDeviceManual), device => device is not null && !IsBusy);
        PingSelectedTypeCommand = new AsyncRelayCommand(PingSelectedTypeAsync, () => DeviceTypeFilter != AllDeviceTypesText && !IsBusy);
        CancelPingCommand = new RelayCommand(CancelPing, () => IsPinging);

        SaveGroupCommand = new AsyncRelayCommand(SaveGroupAsync, () => !IsBusy);
        ClearGroupFormCommand = new RelayCommand(ClearGroupForm, () => !IsBusy);
        EditSelectedGroupCommand = new RelayCommand(() => StartEditGroup(SelectedGroup), () => SelectedGroup is not null && !IsBusy);
        DeleteSelectedGroupCommand = new AsyncRelayCommand(() => DeleteGroupAsync(SelectedGroup), () => SelectedGroup is not null && !IsBusy);
        PingSelectedGroupCommand = new AsyncRelayCommand(() => SelectedGroup is null ? Task.CompletedTask : PingGroupAsync(SelectedGroup), () => SelectedGroup is not null && !IsBusy);

        SaveSchedulePlanCommand = new AsyncRelayCommand(SaveSchedulePlanAsync, () => !IsBusy);
        ClearSchedulePlanFormCommand = new RelayCommand(ClearSchedulePlanForm, () => !IsBusy);
        EditSelectedSchedulePlanCommand = new RelayCommand(() => StartEditSchedulePlan(SelectedSchedulePlan), () => SelectedSchedulePlan is not null && !IsBusy);
        DeleteSelectedSchedulePlanCommand = new AsyncRelayCommand(() => DeleteSchedulePlanAsync(SelectedSchedulePlan), () => SelectedSchedulePlan is not null && !IsBusy);
        RunSelectedSchedulePlanCommand = new AsyncRelayCommand(() => SelectedSchedulePlan is null ? Task.CompletedTask : RunSchedulePlanNowAsync(SelectedSchedulePlan), () => SelectedSchedulePlan is not null && !IsBusy);
        StartSchedulerCommand = new AsyncRelayCommand(StartSchedulerAsync, () => !IsSchedulerRunning && !IsBusy);
        StopSchedulerCommand = new AsyncRelayCommand(StopSchedulerAsync, () => IsSchedulerRunning && !IsBusy);

        RefreshLogsCommand = new AsyncRelayCommand(LoadLogsAsync, () => !IsBusy);
        ClearLogsCommand = new AsyncRelayCommand(ClearLogsAsync, () => Logs.Count > 0 && !IsBusy);
        ClearOldLogsCommand = new AsyncRelayCommand(ClearOldLogsAsync, () => !IsBusy && LogRetentionDays > 0);
        ExportLogsCommand = new AsyncRelayCommand(ExportLogsAsync, () => Logs.Count > 0 && !IsBusy);
        RefreshAvailabilityCommand = new AsyncRelayCommand(LoadAvailabilityAsync, () => !IsBusy);
        ExportAvailabilityCommand = new AsyncRelayCommand(ExportAvailabilityAsync, () => AvailabilityItems.Count > 0 && !IsBusy);
        ExportDevicesCommand = new AsyncRelayCommand(ExportDevicesAsync, () => Devices.Count > 0 && !IsBusy);
        ImportDevicesCommand = new AsyncRelayCommand(ImportDevicesAsync, () => !IsBusy);
        CreateCsvTemplateCommand = new AsyncRelayCommand(CreateCsvTemplateAsync, () => !IsBusy);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, () => !IsBusy);
        ResetSettingsCommand = new AsyncRelayCommand(ResetSettingsAsync, () => !IsBusy);
        BackupDatabaseCommand = new AsyncRelayCommand(BackupDatabaseAsync, () => !IsBusy);
        RestoreDatabaseCommand = new AsyncRelayCommand(RestoreDatabaseAsync, () => !IsBusy);
        ExportSettingsCommand = new AsyncRelayCommand(ExportSettingsAsync, () => !IsBusy);
        ImportSettingsCommand = new AsyncRelayCommand(ImportSettingsAsync, () => !IsBusy);

        _schedulerService.StatusChanged += SchedulerStatusChanged;
        EnsureSummaryCards();
        UpdatePlanTargetOptions();
    }

    public ObservableCollection<Device> Devices { get; } = new();

    public ObservableCollection<DeviceGroup> DeviceGroups { get; } = new();

    public ObservableCollection<SchedulePlan> SchedulePlans { get; } = new();

    public ObservableCollection<PingLog> Logs { get; } = new();

    public ObservableCollection<AvailabilityReportItem> AvailabilityItems { get; } = new();

    public ObservableCollection<Outage> OpenOutages { get; } = new();

    public ObservableCollection<SummaryCardViewModel> SummaryCards { get; } = new();

    public ObservableCollection<Device> CriticalProblemDevices { get; } = new();

    public ObservableCollection<PingLog> RecentFailureLogs { get; } = new();

    public ObservableCollection<PingLog> RecentDashboardLogs { get; } = new();

    public ObservableCollection<AvailabilityReportItem> LowAvailabilityDevices { get; } = new();

    public ObservableCollection<MetricRowViewModel> DeviceTypeDistribution { get; } = new();

    public ObservableCollection<MetricRowViewModel> GroupAvailabilityRows { get; } = new();

    public ObservableCollection<DeviceTypeOption> DeviceTypeOptions { get; }

    public ObservableCollection<string> DeviceTypeFilterOptions { get; }

    public ObservableCollection<string> DeviceStatusFilterOptions { get; }

    public ObservableCollection<string> DeviceGroupFilterOptions { get; } = new(new[] { AllGroupsText });

    public ObservableCollection<string> CriticalFilterOptions { get; }

    public ObservableCollection<string> TriggerFilterOptions { get; }

    public ObservableCollection<SelectionOption<int?>> DeviceGroupOptions { get; } = new();

    public ObservableCollection<SelectionOption<int?>> SchedulePlanOptions { get; } = new();

    public ObservableCollection<SelectionOption<SchedulePlanTargetType>> PlanTargetTypeOptions { get; }

    public ObservableCollection<SelectionOption<string>> PlanTargetOptions { get; } = new();

    public ObservableCollection<string> FrequencyUnitOptions { get; }

    public ObservableCollection<string> ThemeOptions { get; }

    public ICollectionView DevicesView { get; }

    public ICollectionView LogsView { get; }

    public RelayCommand NavigateDashboardCommand { get; }
    public RelayCommand NavigateDevicesCommand { get; }
    public RelayCommand NavigateDeviceEditCommand { get; }
    public RelayCommand NavigateGroupsCommand { get; }
    public RelayCommand NavigateSchedulesCommand { get; }
    public RelayCommand NavigateAvailabilityCommand { get; }
    public RelayCommand NavigateLogsCommand { get; }
    public RelayCommand NavigateSettingsCommand { get; }
    public AsyncRelayCommand SaveDeviceCommand { get; }
    public RelayCommand ClearDeviceFormCommand { get; }
    public RelayCommand EditSelectedDeviceCommand { get; }
    public RelayCommand<Device> EditDeviceCommand { get; }
    public AsyncRelayCommand DeleteSelectedDeviceCommand { get; }
    public AsyncRelayCommand<Device> DeleteDeviceCommand { get; }
    public AsyncRelayCommand PingAllCommand { get; }
    public AsyncRelayCommand PingFilteredDevicesCommand { get; }
    public AsyncRelayCommand PingSelectedDeviceCommand { get; }
    public AsyncRelayCommand<Device> PingDeviceCommand { get; }
    public AsyncRelayCommand PingSelectedTypeCommand { get; }
    public RelayCommand CancelPingCommand { get; }
    public AsyncRelayCommand SaveGroupCommand { get; }
    public RelayCommand ClearGroupFormCommand { get; }
    public RelayCommand EditSelectedGroupCommand { get; }
    public AsyncRelayCommand DeleteSelectedGroupCommand { get; }
    public AsyncRelayCommand PingSelectedGroupCommand { get; }
    public AsyncRelayCommand SaveSchedulePlanCommand { get; }
    public RelayCommand ClearSchedulePlanFormCommand { get; }
    public RelayCommand EditSelectedSchedulePlanCommand { get; }
    public AsyncRelayCommand DeleteSelectedSchedulePlanCommand { get; }
    public AsyncRelayCommand RunSelectedSchedulePlanCommand { get; }
    public AsyncRelayCommand StartSchedulerCommand { get; }
    public AsyncRelayCommand StopSchedulerCommand { get; }
    public AsyncRelayCommand RefreshLogsCommand { get; }
    public AsyncRelayCommand ClearLogsCommand { get; }
    public AsyncRelayCommand ClearOldLogsCommand { get; }
    public AsyncRelayCommand ExportLogsCommand { get; }
    public AsyncRelayCommand RefreshAvailabilityCommand { get; }
    public AsyncRelayCommand ExportAvailabilityCommand { get; }
    public AsyncRelayCommand ExportDevicesCommand { get; }
    public AsyncRelayCommand ImportDevicesCommand { get; }
    public AsyncRelayCommand CreateCsvTemplateCommand { get; }
    public AsyncRelayCommand SaveSettingsCommand { get; }
    public AsyncRelayCommand ResetSettingsCommand { get; }
    public AsyncRelayCommand BackupDatabaseCommand { get; }
    public AsyncRelayCommand RestoreDatabaseCommand { get; }
    public AsyncRelayCommand ExportSettingsCommand { get; }
    public AsyncRelayCommand ImportSettingsCommand { get; }

    public string CurrentSection
    {
        get => _currentSection;
        set
        {
            if (SetProperty(ref _currentSection, value))
            {
                OnPropertyChanged(nameof(SectionTitle));
                OnPropertyChanged(nameof(SectionSubtitle));
                OnPropertyChanged(nameof(IsDashboardSection));
                OnPropertyChanged(nameof(IsDevicesSection));
                OnPropertyChanged(nameof(IsDeviceEditSection));
                OnPropertyChanged(nameof(IsGroupsSection));
                OnPropertyChanged(nameof(IsSchedulesSection));
                OnPropertyChanged(nameof(IsAvailabilitySection));
                OnPropertyChanged(nameof(IsLogsSection));
                OnPropertyChanged(nameof(IsSettingsSection));
            }
        }
    }

    public string SectionTitle => CurrentSection;

    public string SectionSubtitle => CurrentSection switch
    {
        SectionDashboard => "Ağın genel durumunu, kritik cihazları ve son kontrolleri izleyin.",
        SectionDevices => "Manuel eklenen cihazları arayın, filtreleyin ve hızlı ping işlemleri yapın.",
        SectionDeviceEdit => "Cihaz ekleme ve düzenleme işlemleri bu ayrı formdan yapılır.",
        SectionGroups => "Kullanıcı tanımlı grupları yönetin ve grup bazlı ping çalıştırın.",
        SectionSchedules => "Cihaz, tip, grup veya kritik cihaz bazlı otomatik kontrol planları oluşturun.",
        SectionAvailability => "Ping tabanlı ölçülen erişilebilirlik oranlarını ve kesintileri takip edin.",
        SectionLogs => "Ping geçmişini filtreleyin, temizleyin ve CSV olarak dışa aktarın.",
        SectionSettings => "Genel uygulama davranışlarını ve veri bakımını yönetin.",
        _ => string.Empty
    };

    public bool IsDashboardSection => CurrentSection == SectionDashboard;
    public bool IsDevicesSection => CurrentSection == SectionDevices;
    public bool IsDeviceEditSection => CurrentSection == SectionDeviceEdit;
    public bool IsGroupsSection => CurrentSection == SectionGroups;
    public bool IsSchedulesSection => CurrentSection == SectionSchedules;
    public bool IsAvailabilitySection => CurrentSection == SectionAvailability;
    public bool IsLogsSection => CurrentSection == SectionLogs;
    public bool IsSettingsSection => CurrentSection == SectionSettings;

    public bool IsBusy
    {
        get => _isBusy;
        set
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
        set
        {
            if (SetProperty(ref _isPinging, value))
            {
                OnPropertyChanged(nameof(CanShowPingProgress));
                RaiseCommandStates();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value ?? string.Empty);
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

    public DeviceGroup? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (SetProperty(ref _selectedGroup, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public SchedulePlan? SelectedSchedulePlan
    {
        get => _selectedSchedulePlan;
        set
        {
            if (SetProperty(ref _selectedSchedulePlan, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string DeviceSearchText
    {
        get => _deviceSearchText;
        set
        {
            if (SetProperty(ref _deviceSearchText, value ?? string.Empty))
            {
                DevicesView.Refresh();
            }
        }
    }

    public string DeviceTypeFilter
    {
        get => _deviceTypeFilter;
        set
        {
            if (SetProperty(ref _deviceTypeFilter, value ?? AllDeviceTypesText))
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
            if (SetProperty(ref _deviceStatusFilter, value ?? AllStatusesText))
            {
                DevicesView.Refresh();
            }
        }
    }

    public string DeviceGroupFilter
    {
        get => _deviceGroupFilter;
        set
        {
            if (SetProperty(ref _deviceGroupFilter, value ?? AllGroupsText))
            {
                DevicesView.Refresh();
            }
        }
    }

    public string CriticalFilter
    {
        get => _criticalFilter;
        set
        {
            if (SetProperty(ref _criticalFilter, value ?? AllCriticalText))
            {
                DevicesView.Refresh();
            }
        }
    }

    public DateTime? LogStartDate
    {
        get => _logStartDate;
        set => SetProperty(ref _logStartDate, value);
    }

    public DateTime? LogEndDate
    {
        get => _logEndDate;
        set => SetProperty(ref _logEndDate, value);
    }

    public string LogDeviceNameFilter
    {
        get => _logDeviceNameFilter;
        set => SetProperty(ref _logDeviceNameFilter, value ?? string.Empty);
    }

    public string LogIpAddressFilter
    {
        get => _logIpAddressFilter;
        set => SetProperty(ref _logIpAddressFilter, value ?? string.Empty);
    }

    public string LogDeviceTypeFilter
    {
        get => _logDeviceTypeFilter;
        set => SetProperty(ref _logDeviceTypeFilter, value ?? AllDeviceTypesText);
    }

    public string LogStatusFilter
    {
        get => _logStatusFilter;
        set => SetProperty(ref _logStatusFilter, value ?? AllStatusesText);
    }

    public string LogGroupFilter
    {
        get => _logGroupFilter;
        set => SetProperty(ref _logGroupFilter, value ?? AllGroupsText);
    }

    public string LogTriggerFilter
    {
        get => _logTriggerFilter;
        set => SetProperty(ref _logTriggerFilter, value ?? AllTriggersText);
    }

    public string LogPlanNameFilter
    {
        get => _logPlanNameFilter;
        set => SetProperty(ref _logPlanNameFilter, value ?? string.Empty);
    }

    public bool LogOnlyUnreachable
    {
        get => _logOnlyUnreachable;
        set => SetProperty(ref _logOnlyUnreachable, value);
    }

    public string FormName
    {
        get => _formName;
        set => SetProperty(ref _formName, value ?? string.Empty);
    }

    public string FormIpAddress
    {
        get => _formIpAddress;
        set => SetProperty(ref _formIpAddress, value ?? string.Empty);
    }

    public DeviceType FormDeviceType
    {
        get => _formDeviceType;
        set => SetProperty(ref _formDeviceType, value);
    }

    public int? FormGroupId
    {
        get => _formGroupId;
        set => SetProperty(ref _formGroupId, value);
    }

    public string FormLocation
    {
        get => _formLocation;
        set => SetProperty(ref _formLocation, value ?? string.Empty);
    }

    public string FormDescription
    {
        get => _formDescription;
        set => SetProperty(ref _formDescription, value ?? string.Empty);
    }

    public bool FormAutoCheckEnabled
    {
        get => _formAutoCheckEnabled;
        set => SetProperty(ref _formAutoCheckEnabled, value);
    }

    public int? FormDefaultSchedulePlanId
    {
        get => _formDefaultSchedulePlanId;
        set => SetProperty(ref _formDefaultSchedulePlanId, value);
    }

    public bool FormIsCritical
    {
        get => _formIsCritical;
        set => SetProperty(ref _formIsCritical, value);
    }

    public bool FormIsActive
    {
        get => _formIsActive;
        set => SetProperty(ref _formIsActive, value);
    }

    public string DeviceFormTitle => _editingDeviceId.HasValue ? "Cihazı Düzenle" : "Yeni Cihaz";

    public string DeviceFormActionText => _editingDeviceId.HasValue ? "Güncelle" : "Kaydet";

    public string GroupFormName
    {
        get => _groupFormName;
        set => SetProperty(ref _groupFormName, value ?? string.Empty);
    }

    public string GroupFormDescription
    {
        get => _groupFormDescription;
        set => SetProperty(ref _groupFormDescription, value ?? string.Empty);
    }

    public int? GroupFormDefaultSchedulePlanId
    {
        get => _groupFormDefaultSchedulePlanId;
        set => SetProperty(ref _groupFormDefaultSchedulePlanId, value);
    }

    public string GroupFormTitle => _editingGroupId.HasValue ? "Grubu Düzenle" : "Yeni Grup";

    public string GroupFormActionText => _editingGroupId.HasValue ? "Güncelle" : "Kaydet";

    public string PlanFormName
    {
        get => _planFormName;
        set => SetProperty(ref _planFormName, value ?? string.Empty);
    }

    public SchedulePlanTargetType PlanFormTargetType
    {
        get => _planFormTargetType;
        set
        {
            if (SetProperty(ref _planFormTargetType, value))
            {
                UpdatePlanTargetOptions();
            }
        }
    }

    public string PlanFormTargetValue
    {
        get => _planFormTargetValue;
        set => SetProperty(ref _planFormTargetValue, value ?? string.Empty);
    }

    public int PlanFormFrequencyValue
    {
        get => _planFormFrequencyValue;
        set => SetProperty(ref _planFormFrequencyValue, Math.Max(1, value));
    }

    public string PlanFormFrequencyUnit
    {
        get => _planFormFrequencyUnit;
        set => SetProperty(ref _planFormFrequencyUnit, value ?? "Dakika");
    }

    public int PlanFormTimeoutMs
    {
        get => _planFormTimeoutMs;
        set => SetProperty(ref _planFormTimeoutMs, value);
    }

    public int PlanFormMaxParallelism
    {
        get => _planFormMaxParallelism;
        set => SetProperty(ref _planFormMaxParallelism, value);
    }

    public int PlanFormFailureThreshold
    {
        get => _planFormFailureThreshold;
        set => SetProperty(ref _planFormFailureThreshold, value);
    }

    public bool PlanFormIsActive
    {
        get => _planFormIsActive;
        set => SetProperty(ref _planFormIsActive, value);
    }

    public string PlanFormDescription
    {
        get => _planFormDescription;
        set => SetProperty(ref _planFormDescription, value ?? string.Empty);
    }

    public string PlanFormTitle => _editingPlanId.HasValue ? "Planı Düzenle" : "Yeni Plan";

    public string PlanFormActionText => _editingPlanId.HasValue ? "Güncelle" : "Kaydet";

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

    public int DefaultFailureThreshold
    {
        get => _defaultFailureThreshold;
        set => SetProperty(ref _defaultFailureThreshold, value);
    }

    public bool StartSchedulePlansOnStartup
    {
        get => _startSchedulePlansOnStartup;
        set => SetProperty(ref _startSchedulePlansOnStartup, value);
    }

    public string CsvDelimiter
    {
        get => _csvDelimiter;
        set => SetProperty(ref _csvDelimiter, value ?? ";");
    }

    public int LogRetentionDays
    {
        get => _logRetentionDays;
        set => SetProperty(ref _logRetentionDays, value);
    }

    public string Theme
    {
        get => _theme;
        set => SetProperty(ref _theme, value ?? "Açık");
    }

    public bool IsSchedulerRunning
    {
        get => _isSchedulerRunning;
        private set
        {
            if (SetProperty(ref _isSchedulerRunning, value))
            {
                OnPropertyChanged(nameof(SchedulerStatusText));
                RaiseCommandStates();
            }
        }
    }

    public string SchedulerStatusText => IsSchedulerRunning ? "Çalışıyor" : "Durduruldu";

    public int PingTotalCount
    {
        get => _pingTotalCount;
        set
        {
            if (SetProperty(ref _pingTotalCount, value))
            {
                OnPropertyChanged(nameof(PingProgressPercent));
                OnPropertyChanged(nameof(CanShowPingProgress));
            }
        }
    }

    public int PingCompletedCount
    {
        get => _pingCompletedCount;
        set
        {
            if (SetProperty(ref _pingCompletedCount, value))
            {
                OnPropertyChanged(nameof(PingProgressPercent));
            }
        }
    }

    public int PingSuccessCount
    {
        get => _pingSuccessCount;
        set => SetProperty(ref _pingSuccessCount, value);
    }

    public int PingFailureCount
    {
        get => _pingFailureCount;
        set => SetProperty(ref _pingFailureCount, value);
    }

    public double PingProgressPercent => PingTotalCount == 0 ? 0 : PingCompletedCount * 100d / PingTotalCount;

    public bool CanShowPingProgress => IsPinging && PingTotalCount > 0;

    public string DatabaseLocation => DatabasePaths.DatabaseFilePath;

    public string AvailabilityNotice => "Bu ekran ping tabanlı ölçülen erişilebilirliği gösterir. Gerçek cihaz uptime değeri yalnızca cihaz yetkili bir yöntemle desteklerse ayrı kaynaklardan alınabilir.";

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        IsBusy = true;
        try
        {
            ApplySettings(await _settingsService.LoadAsync());
            await ReloadAllAsync();
            if (StartSchedulePlansOnStartup)
            {
                await StartSchedulerAsync();
            }

            StatusMessage = "Hazır.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _pingCancellationTokenSource?.Cancel();
        _pingCancellationTokenSource?.Dispose();
        await _schedulerService.StopAsync();
        _schedulerService.StatusChanged -= SchedulerStatusChanged;
    }

    private async Task ReloadAllAsync()
    {
        await LoadDevicesAsync();
        await LoadGroupsAsync();
        await LoadSchedulePlansAsync();
        await LoadAvailabilityAsync();
        await LoadOpenOutagesAsync();
        await LoadLogsAsync();
        UpdateDashboard();
        RaiseCommandStates();
    }

    private async Task LoadDevicesAsync()
    {
        var devices = await _deviceRepository.GetAllAsync();
        var metrics = await _pingLogRepository.GetDeviceHealthMetricsAsync(DateTime.Now.AddDays(-30));
        foreach (var device in devices)
        {
            if (!metrics.TryGetValue(device.Id, out var metric))
            {
                continue;
            }

            device.Uptime24HoursPercent = metric.Uptime24HoursPercent;
            device.Uptime7DaysPercent = metric.Uptime7DaysPercent;
            device.Uptime30DaysPercent = metric.Uptime30DaysPercent;
            device.AverageLatencyMs = metric.AverageLatencyMs;
            device.LastFailureAt = metric.LastFailureAt;
        }

        ReplaceCollection(Devices, devices);
        DevicesView.Refresh();
    }

    private async Task LoadGroupsAsync()
    {
        var groups = await _deviceGroupRepository.GetAllAsync();
        var availabilityByGroup = AvailabilityItems
            .Where(item => !string.IsNullOrWhiteSpace(item.GroupName) && item.Availability30DaysPercent.HasValue)
            .GroupBy(item => item.GroupName)
            .ToDictionary(
                group => group.Key,
                group => (double?)group.Average(item => item.Availability30DaysPercent!.Value),
                StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            if (availabilityByGroup.TryGetValue(group.Name, out var availability))
            {
                group.Availability30DaysPercent = availability;
            }
        }

        ReplaceCollection(DeviceGroups, groups);
        RefreshGroupOptions();
        UpdatePlanTargetOptions(keepCurrentValue: true);
    }

    private async Task LoadSchedulePlansAsync()
    {
        var plans = await _schedulePlanRepository.GetAllAsync();
        foreach (var plan in plans)
        {
            plan.TargetDisplayName = ResolvePlanTargetDisplayName(plan);
        }

        ReplaceCollection(SchedulePlans, plans);
        RefreshSchedulePlanOptions();
        UpdatePlanTargetOptions(keepCurrentValue: true);
    }

    private async Task LoadAvailabilityAsync()
    {
        var items = await _availabilityService.GetDeviceAvailabilityAsync(DateTime.Now.AddDays(-30));
        ReplaceCollection(AvailabilityItems, items);
        ApplyGroupAvailabilityToGroups();
        UpdateGroupAvailabilityRows();
        UpdateDashboard();
    }

    private async Task LoadOpenOutagesAsync()
    {
        ReplaceCollection(OpenOutages, await _outageRepository.GetOpenAsync());
    }

    private async Task LoadLogsAsync()
    {
        var logs = await _pingLogRepository.GetFilteredAsync(
            LogStartDate,
            LogEndDate,
            LogDeviceNameFilter,
            LogIpAddressFilter,
            ParseDeviceTypeFilter(LogDeviceTypeFilter),
            ParseStatusFilter(LogStatusFilter),
            LogGroupFilter == AllGroupsText ? null : LogGroupFilter,
            ParseTriggerFilter(LogTriggerFilter),
            LogPlanNameFilter,
            LogOnlyUnreachable,
            5000);

        ReplaceCollection(Logs, logs);
        LogsView.Refresh();
        UpdateDashboard();
        RaiseCommandStates();
    }

    private async Task SaveDeviceAsync()
    {
        var groupName = ResolveGroupName(FormGroupId);
        var device = _editingDeviceId.HasValue
            ? Devices.FirstOrDefault(item => item.Id == _editingDeviceId.Value) ?? new Device { Id = _editingDeviceId.Value }
            : new Device();

        device.Name = FormName;
        device.IpAddress = FormIpAddress;
        device.DeviceType = FormDeviceType;
        device.GroupId = FormGroupId;
        device.GroupName = groupName;
        device.Location = FormLocation;
        device.Description = FormDescription;
        device.AutoCheckEnabled = FormAutoCheckEnabled;
        device.DefaultSchedulePlanId = FormDefaultSchedulePlanId;
        device.IsCritical = FormIsCritical;
        device.IsActive = FormIsActive;

        IsBusy = true;
        try
        {
            var result = await _deviceService.SaveAsync(device);
            if (!result.Success)
            {
                _dialogService.ShowWarning("Cihaz kaydedilemedi", result.Message);
                return;
            }

            StatusMessage = result.Message;
            ClearDeviceForm();
            await ReloadAllAsync();
            CurrentSection = SectionDevices;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void StartEditDevice(Device? device)
    {
        if (device is null)
        {
            return;
        }

        _editingDeviceId = device.Id;
        FormName = device.Name;
        FormIpAddress = device.IpAddress;
        FormDeviceType = device.DeviceType;
        FormGroupId = device.GroupId;
        FormLocation = device.Location;
        FormDescription = device.Description;
        FormAutoCheckEnabled = device.AutoCheckEnabled;
        FormDefaultSchedulePlanId = device.DefaultSchedulePlanId;
        FormIsCritical = device.IsCritical;
        FormIsActive = device.IsActive;
        OnPropertyChanged(nameof(DeviceFormTitle));
        OnPropertyChanged(nameof(DeviceFormActionText));
        CurrentSection = SectionDeviceEdit;
    }

    private void ClearDeviceForm()
    {
        _editingDeviceId = null;
        FormName = string.Empty;
        FormIpAddress = string.Empty;
        FormDeviceType = DeviceType.Camera;
        FormGroupId = null;
        FormLocation = string.Empty;
        FormDescription = string.Empty;
        FormAutoCheckEnabled = true;
        FormDefaultSchedulePlanId = null;
        FormIsCritical = false;
        FormIsActive = true;
        OnPropertyChanged(nameof(DeviceFormTitle));
        OnPropertyChanged(nameof(DeviceFormActionText));
    }

    private async Task DeleteDeviceAsync(Device? device)
    {
        if (device is null)
        {
            return;
        }

        if (!_dialogService.Confirm("Cihaz silinsin mi?", $"{device.Name} cihazı silinecek. Ping logları korunur."))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _deviceService.DeleteAsync(device);
            StatusMessage = result.Message;
            await ReloadAllAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveGroupAsync()
    {
        var group = _editingGroupId.HasValue
            ? DeviceGroups.FirstOrDefault(item => item.Id == _editingGroupId.Value) ?? new DeviceGroup { Id = _editingGroupId.Value }
            : new DeviceGroup();

        group.Name = GroupFormName;
        group.Description = GroupFormDescription;
        group.DefaultSchedulePlanId = GroupFormDefaultSchedulePlanId;

        IsBusy = true;
        try
        {
            var result = await _deviceGroupService.SaveAsync(group);
            if (!result.Success)
            {
                _dialogService.ShowWarning("Grup kaydedilemedi", result.Message);
                return;
            }

            StatusMessage = result.Message;
            ClearGroupForm();
            await ReloadAllAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void StartEditGroup(DeviceGroup? group)
    {
        if (group is null)
        {
            return;
        }

        _editingGroupId = group.Id;
        GroupFormName = group.Name;
        GroupFormDescription = group.Description;
        GroupFormDefaultSchedulePlanId = group.DefaultSchedulePlanId;
        OnPropertyChanged(nameof(GroupFormTitle));
        OnPropertyChanged(nameof(GroupFormActionText));
    }

    private void ClearGroupForm()
    {
        _editingGroupId = null;
        GroupFormName = string.Empty;
        GroupFormDescription = string.Empty;
        GroupFormDefaultSchedulePlanId = null;
        OnPropertyChanged(nameof(GroupFormTitle));
        OnPropertyChanged(nameof(GroupFormActionText));
    }

    private async Task DeleteGroupAsync(DeviceGroup? group)
    {
        if (group is null)
        {
            return;
        }

        if (!_dialogService.Confirm("Grup silinsin mi?", $"{group.Name} grubu silinecek. Cihazlar silinmez, sadece grup bağlantısı kaldırılır."))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _deviceGroupService.DeleteAsync(group);
            StatusMessage = result.Message;
            await ReloadAllAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveSchedulePlanAsync()
    {
        var plan = _editingPlanId.HasValue
            ? SchedulePlans.FirstOrDefault(item => item.Id == _editingPlanId.Value) ?? new SchedulePlan { Id = _editingPlanId.Value }
            : new SchedulePlan();

        plan.Name = PlanFormName;
        plan.TargetType = PlanFormTargetType;
        plan.TargetValue = PlanFormTargetValue;
        plan.IntervalMinutes = ConvertFrequencyToMinutes(PlanFormFrequencyValue, PlanFormFrequencyUnit);
        plan.TimeoutMs = PlanFormTimeoutMs;
        plan.MaxParallelism = PlanFormMaxParallelism;
        plan.FailureThreshold = PlanFormFailureThreshold;
        plan.IsActive = PlanFormIsActive;
        plan.Description = PlanFormDescription;

        IsBusy = true;
        try
        {
            var result = await _schedulePlanService.SaveAsync(plan);
            if (!result.Success)
            {
                _dialogService.ShowWarning("Plan kaydedilemedi", result.Message);
                return;
            }

            StatusMessage = result.Message;
            ClearSchedulePlanForm();
            await ReloadAllAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void StartEditSchedulePlan(SchedulePlan? plan)
    {
        if (plan is null)
        {
            return;
        }

        _editingPlanId = plan.Id;
        PlanFormName = plan.Name;
        PlanFormTargetType = plan.TargetType;
        UpdatePlanTargetOptions();
        PlanFormTargetValue = plan.TargetValue;
        ApplyFrequency(plan.IntervalMinutes);
        PlanFormTimeoutMs = plan.TimeoutMs;
        PlanFormMaxParallelism = plan.MaxParallelism;
        PlanFormFailureThreshold = plan.FailureThreshold;
        PlanFormIsActive = plan.IsActive;
        PlanFormDescription = plan.Description;
        OnPropertyChanged(nameof(PlanFormTitle));
        OnPropertyChanged(nameof(PlanFormActionText));
    }

    private void ClearSchedulePlanForm()
    {
        _editingPlanId = null;
        PlanFormName = string.Empty;
        PlanFormTargetType = SchedulePlanTargetType.AllDevices;
        PlanFormTargetValue = string.Empty;
        PlanFormFrequencyValue = 10;
        PlanFormFrequencyUnit = "Dakika";
        PlanFormTimeoutMs = PingTimeoutMs;
        PlanFormMaxParallelism = Math.Min(MaxParallelPings, 16);
        PlanFormFailureThreshold = DefaultFailureThreshold;
        PlanFormIsActive = true;
        PlanFormDescription = string.Empty;
        UpdatePlanTargetOptions();
        OnPropertyChanged(nameof(PlanFormTitle));
        OnPropertyChanged(nameof(PlanFormActionText));
    }

    private async Task DeleteSchedulePlanAsync(SchedulePlan? plan)
    {
        if (plan is null)
        {
            return;
        }

        if (!_dialogService.Confirm("Plan silinsin mi?", $"{plan.Name} otomatik kontrol planı silinecek."))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _schedulePlanService.DeleteAsync(plan);
            StatusMessage = result.Message;
            await ReloadAllAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunSchedulePlanNowAsync(SchedulePlan plan)
    {
        var targets = ResolveTargetsForPlan(plan, respectAutoCheck: false).ToList();
        if (targets.Count == 0)
        {
            _dialogService.ShowWarning("Hedef bulunamadı", "Bu plan için kontrol edilecek aktif cihaz bulunamadı.");
            return;
        }

        await RunManualPingAsync(targets, PingTriggerType.Scheduled, plan);
        await _schedulePlanRepository.UpdateLastRunAsync(plan.Id, DateTime.Now);
        await LoadSchedulePlansAsync();
    }

    private async Task PingGroupAsync(DeviceGroup group)
    {
        var devices = Devices.Where(device => device.GroupId == group.Id).ToList();
        if (devices.Count == 0)
        {
            _dialogService.ShowWarning("Cihaz bulunamadı", "Bu gruba atanmış cihaz bulunmuyor.");
            return;
        }

        await RunManualPingAsync(devices, PingTriggerType.GroupManual);
    }

    private async Task PingSelectedTypeAsync()
    {
        var type = ParseDeviceTypeFilter(DeviceTypeFilter);
        if (!type.HasValue)
        {
            return;
        }

        await RunManualPingAsync(Devices.Where(device => device.DeviceType == type.Value).ToList(), PingTriggerType.TypeManual);
    }

    private async Task RunManualPingAsync(
        IEnumerable<Device> devices,
        PingTriggerType triggerType,
        SchedulePlan? schedulePlan = null)
    {
        var targets = devices.Where(device => device.IsActive).DistinctBy(device => device.Id).ToList();
        if (targets.Count == 0)
        {
            _dialogService.ShowWarning("Cihaz bulunamadı", "Kontrol edilecek aktif cihaz bulunamadı.");
            return;
        }

        _pingCancellationTokenSource?.Cancel();
        _pingCancellationTokenSource?.Dispose();
        _pingCancellationTokenSource = new CancellationTokenSource();
        ResetPingProgress(targets.Count);
        IsPinging = true;
        IsBusy = true;
        StatusMessage = $"{targets.Count} cihaz kontrol ediliyor...";

        var progress = new Progress<PingProgress>(ApplyPingProgress);
        try
        {
            var options = schedulePlan?.ToPingOptions() ?? new PingOptions(PingTimeoutMs, MaxParallelPings, DefaultFailureThreshold);
            var result = await _pingExecutionService.PingDevicesAsync(
                targets,
                options,
                triggerType,
                schedulePlan,
                progress,
                _pingCancellationTokenSource.Token);

            StatusMessage = $"Ping tamamlandı. Başarılı: {result.SuccessCount}, başarısız: {result.FailureCount}, atlanan: {result.SkippedBecauseAlreadyRunning}.";
            await ReloadAllAsync();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Ping işlemi iptal edildi.";
        }
        finally
        {
            IsPinging = false;
            IsBusy = false;
        }
    }

    private void CancelPing()
    {
        _pingCancellationTokenSource?.Cancel();
    }

    private void ApplyPingProgress(PingProgress progress)
    {
        PingTotalCount = progress.Total;
        PingCompletedCount = progress.Completed;
        PingSuccessCount = progress.Success;
        PingFailureCount = progress.Failure;

        if (progress.DeviceId.HasValue && progress.DeviceStatus.HasValue)
        {
            var device = Devices.FirstOrDefault(item => item.Id == progress.DeviceId.Value);
            if (device is not null)
            {
                device.LastStatus = progress.DeviceStatus.Value;
                if (progress.LatencyMs.HasValue || progress.DeviceStatus.Value != DeviceStatus.Checking)
                {
                    device.LastLatencyMs = progress.LatencyMs;
                }

                if (progress.CheckedAt.HasValue)
                {
                    device.LastCheckedAt = progress.CheckedAt;
                }
            }
        }

        StatusMessage = $"Kontrol ediliyor: {progress.Completed}/{progress.Total} tamamlandı.";
    }

    private void ResetPingProgress(int total)
    {
        PingTotalCount = total;
        PingCompletedCount = 0;
        PingSuccessCount = 0;
        PingFailureCount = 0;
    }

    private async Task StartSchedulerAsync()
    {
        await _schedulerService.StartAsync();
        IsSchedulerRunning = _schedulerService.IsRunning;
    }

    private async Task StopSchedulerAsync()
    {
        await _schedulerService.StopAsync();
        IsSchedulerRunning = _schedulerService.IsRunning;
    }

    private void SchedulerStatusChanged(object? sender, SchedulerStatusChangedEventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            StatusMessage = e.Message;
            IsSchedulerRunning = _schedulerService.IsRunning;
            return;
        }

        dispatcher.BeginInvoke(() =>
        {
            StatusMessage = e.Message;
            IsSchedulerRunning = _schedulerService.IsRunning;
            _ = RefreshAfterSchedulerAsync();
        });
    }

    private async Task RefreshAfterSchedulerAsync()
    {
        if (IsBusy || IsPinging)
        {
            return;
        }

        await LoadDevicesAsync();
        await LoadAvailabilityAsync();
        await LoadOpenOutagesAsync();
        await LoadLogsAsync();
    }

    private async Task ClearLogsAsync()
    {
        if (!_dialogService.Confirm("Tüm loglar temizlensin mi?", "Bu işlem tüm ping loglarını siler. Cihaz kayıtları korunur."))
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _pingLogRepository.ClearAsync();
            await ReloadAllAsync();
            StatusMessage = "Tüm ping logları temizlendi.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ClearOldLogsAsync()
    {
        if (LogRetentionDays <= 0)
        {
            _dialogService.ShowWarning("Geçersiz süre", "Log saklama süresi 0'dan büyük olmalıdır.");
            return;
        }

        if (!_dialogService.Confirm("Eski loglar temizlensin mi?", $"{LogRetentionDays} günden eski ping logları silinecek."))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var deleted = await _pingLogRepository.ClearOlderThanAsync(DateTime.Now.AddDays(-LogRetentionDays));
            await ReloadAllAsync();
            StatusMessage = $"{deleted} eski log temizlendi.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportDevicesAsync()
    {
        var path = _dialogService.GetSaveCsvFilePath($"cihaz-listesi-{DateTime.Now:yyyy-MM-dd}.csv");
        if (path is null)
        {
            return;
        }

        await _csvExportService.ExportDevicesAsync(DevicesView.Cast<Device>().ToList(), path, CsvDelimiter);
        StatusMessage = "Cihaz listesi CSV olarak dışa aktarıldı.";
    }

    private async Task CreateCsvTemplateAsync()
    {
        var path = _dialogService.GetSaveCsvFilePath($"cihaz-import-sablonu-{DateTime.Now:yyyy-MM-dd}.csv");
        if (path is null)
        {
            return;
        }

        await _csvExportService.ExportDeviceTemplateAsync(path, CsvDelimiter);
        StatusMessage = "CSV import şablonu oluşturuldu.";
    }

    private async Task ImportDevicesAsync()
    {
        var path = _dialogService.GetOpenCsvFilePath();
        if (path is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var preview = await _csvExportService.ReadDeviceImportPreviewAsync(path, Devices, CsvDelimiter);
            if (!preview.HasImportableRows)
            {
                _dialogService.ShowWarning("Import yapılamadı", "CSV içinde eklenebilir geçerli cihaz satırı bulunamadı.");
                return;
            }

            var duplicateAction = preview.ExistingIpCount > 0
                ? _dialogService.ChooseDuplicateImportAction("Duplicate IP bulundu", $"{preview.ExistingIpCount} IP adresi veritabanında zaten var.")
                : CsvImportDuplicateAction.UpdateExisting;

            if (duplicateAction == CsvImportDuplicateAction.Cancel)
            {
                StatusMessage = "CSV import iptal edildi.";
                return;
            }

            var result = await _deviceRepository.ImportDevicesAsync(preview.ValidRows, duplicateAction, preview.InvalidRowCount);
            await ReloadAllAsync();
            StatusMessage = $"CSV import tamamlandı. Eklenen: {result.Added}, güncellenen: {result.Updated}, atlanan: {result.Skipped}, hatalı: {result.Invalid}.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportLogsAsync()
    {
        var path = _dialogService.GetSaveCsvFilePath($"ping-loglari-{DateTime.Now:yyyy-MM-dd}.csv");
        if (path is null)
        {
            return;
        }

        await _csvExportService.ExportLogsAsync(Logs.ToList(), path, CsvDelimiter);
        StatusMessage = "Ping logları CSV olarak dışa aktarıldı.";
    }

    private async Task ExportAvailabilityAsync()
    {
        var path = _dialogService.GetSaveCsvFilePath($"uptime-raporu-{DateTime.Now:yyyy-MM-dd}.csv");
        if (path is null)
        {
            return;
        }

        await _csvExportService.ExportAvailabilityAsync(AvailabilityItems.ToList(), path, CsvDelimiter);
        StatusMessage = "Erişilebilirlik raporu CSV olarak dışa aktarıldı.";
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

        if (DefaultFailureThreshold < 1 || DefaultFailureThreshold > 20)
        {
            _dialogService.ShowWarning("Geçersiz ayar", "Varsayılan başarısızlık eşiği 1 ile 20 arasında olmalıdır.");
            return;
        }

        await _settingsService.SaveAsync(new AppSettings
        {
            PingTimeoutMs = PingTimeoutMs,
            MaxParallelPings = MaxParallelPings,
            DefaultFailureThreshold = DefaultFailureThreshold,
            StartSchedulePlansOnStartup = StartSchedulePlansOnStartup,
            CsvDelimiter = CsvDelimiter,
            LogRetentionDays = LogRetentionDays,
            Theme = Theme
        });

        StatusMessage = "Ayarlar kaydedildi.";
    }

    private async Task ResetSettingsAsync()
    {
        if (!_dialogService.Confirm("Ayarlar sıfırlansın mı?", "Genel uygulama ayarları varsayılan değerlere dönecek."))
        {
            return;
        }

        var defaults = AppSettings.Default;
        await _settingsService.SaveAsync(defaults);
        ApplySettings(defaults);
        StatusMessage = "Ayarlar sıfırlandı.";
    }

    private async Task BackupDatabaseAsync()
    {
        var path = _dialogService.GetSaveDatabaseFilePath($"network-health-monitor-{DateTime.Now:yyyyMMdd-HHmm}.db");
        if (path is null)
        {
            return;
        }

        await _maintenanceService.BackupDatabaseAsync(path);
        StatusMessage = "Veritabanı yedeklendi.";
    }

    private async Task RestoreDatabaseAsync()
    {
        var path = _dialogService.GetOpenDatabaseFilePath();
        if (path is null)
        {
            return;
        }

        if (!_dialogService.Confirm("Veritabanı geri yüklensin mi?", "Mevcut veritabanı otomatik yedeklenip seçilen dosya geri yüklenecek."))
        {
            return;
        }

        await StopSchedulerAsync();
        var backupPath = await _maintenanceService.RestoreDatabaseAsync(path);
        await ReloadAllAsync();
        StatusMessage = $"Veritabanı geri yüklendi. Önceki dosya yedeği: {backupPath}";
    }

    private async Task ExportSettingsAsync()
    {
        var path = _dialogService.GetSaveJsonFilePath($"network-health-monitor-settings-{DateTime.Now:yyyyMMdd-HHmm}.json");
        if (path is null)
        {
            return;
        }

        await SaveSettingsAsync();
        await _maintenanceService.ExportSettingsAsync(path);
        StatusMessage = "Ayarlar dışa aktarıldı.";
    }

    private async Task ImportSettingsAsync()
    {
        var path = _dialogService.GetOpenJsonFilePath();
        if (path is null)
        {
            return;
        }

        await _maintenanceService.ImportSettingsAsync(path);
        ApplySettings(await _settingsService.LoadAsync());
        StatusMessage = "Ayarlar içe aktarıldı.";
    }

    private void ApplySettings(AppSettings settings)
    {
        PingTimeoutMs = settings.PingTimeoutMs;
        MaxParallelPings = settings.MaxParallelPings;
        DefaultFailureThreshold = settings.DefaultFailureThreshold;
        StartSchedulePlansOnStartup = settings.StartSchedulePlansOnStartup;
        CsvDelimiter = settings.CsvDelimiter;
        LogRetentionDays = settings.LogRetentionDays;
        Theme = settings.Theme;
        PlanFormTimeoutMs = settings.PingTimeoutMs;
        PlanFormMaxParallelism = Math.Min(settings.MaxParallelPings, 16);
        PlanFormFailureThreshold = settings.DefaultFailureThreshold;
    }

    private void UpdateDashboard()
    {
        if (SummaryCards.Count < 8)
        {
            EnsureSummaryCards();
        }

        var total = Devices.Count;
        var reachable = Devices.Count(device => device.LastStatus == DeviceStatus.Reachable);
        var unreachable = Devices.Count(device => device.LastStatus == DeviceStatus.Unreachable);
        var notChecked = Devices.Count(device => device.LastStatus == DeviceStatus.NotChecked);
        var criticalDown = Devices.Count(device => device.IsCritical && device.LastStatus == DeviceStatus.Unreachable);
        var last24Logs = Logs.Where(log => log.CheckedAt >= DateTime.Now.AddHours(-24)).ToList();
        double? success24 = last24Logs.Count == 0
            ? null
            : last24Logs.Count(log => log.Status == DeviceStatus.Reachable) * 100d / last24Logs.Count;
        var activePlans = SchedulePlans.Count(plan => plan.IsActive);

        SummaryCards[0].Value = total.ToString(CultureInfo.CurrentCulture);
        SummaryCards[1].Value = reachable.ToString(CultureInfo.CurrentCulture);
        SummaryCards[2].Value = unreachable.ToString(CultureInfo.CurrentCulture);
        SummaryCards[3].Value = notChecked.ToString(CultureInfo.CurrentCulture);
        SummaryCards[4].Value = criticalDown.ToString(CultureInfo.CurrentCulture);
        SummaryCards[5].Value = success24.HasValue ? $"{success24.Value:0.0}%" : "-";
        SummaryCards[6].Value = activePlans.ToString(CultureInfo.CurrentCulture);
        SummaryCards[7].Value = OpenOutages.Count.ToString(CultureInfo.CurrentCulture);

        ReplaceCollection(CriticalProblemDevices, Devices.Where(device => device.IsCritical && device.LastStatus == DeviceStatus.Unreachable).Take(10));
        ReplaceCollection(RecentFailureLogs, Logs.Where(log => log.Status == DeviceStatus.Unreachable).Take(10));
        ReplaceCollection(RecentDashboardLogs, Logs.Take(12));
        ReplaceCollection(LowAvailabilityDevices, AvailabilityItems
            .Where(item => item.Availability30DaysPercent.HasValue)
            .OrderBy(item => item.Availability30DaysPercent)
            .Take(10));
        UpdateTypeDistributionRows();
    }

    private void EnsureSummaryCards()
    {
        if (SummaryCards.Count > 0)
        {
            return;
        }

        SummaryCards.Add(new SummaryCardViewModel("Toplam cihaz", "0", "#2563EB"));
        SummaryCards.Add(new SummaryCardViewModel("Erişilebilir", "0", "#16A34A"));
        SummaryCards.Add(new SummaryCardViewModel("Erişilemeyen", "0", "#DC2626"));
        SummaryCards.Add(new SummaryCardViewModel("Kontrol edilmemiş", "0", "#64748B"));
        SummaryCards.Add(new SummaryCardViewModel("Kritik sorun", "0", "#B91C1C"));
        SummaryCards.Add(new SummaryCardViewModel("24s başarı", "-", "#0F766E"));
        SummaryCards.Add(new SummaryCardViewModel("Aktif plan", "0", "#7C3AED"));
        SummaryCards.Add(new SummaryCardViewModel("Açık kesinti", "0", "#EA580C"));
    }

    private void UpdateTypeDistributionRows()
    {
        var total = Math.Max(1, Devices.Count);
        var rows = Devices
            .GroupBy(device => device.DeviceType)
            .OrderBy(group => group.Key.ToDisplayName())
            .Select(group => new MetricRowViewModel
            {
                Title = group.Key.ToDisplayName(),
                Value = group.Count().ToString(CultureInfo.CurrentCulture),
                Percent = group.Count() * 100d / total,
                AccentColor = group.Any(device => device.LastStatus == DeviceStatus.Unreachable) ? "#DC2626" : "#2563EB"
            });

        ReplaceCollection(DeviceTypeDistribution, rows);
    }

    private void UpdateGroupAvailabilityRows()
    {
        var rows = AvailabilityItems
            .Where(item => !string.IsNullOrWhiteSpace(item.GroupName) && item.Availability30DaysPercent.HasValue)
            .GroupBy(item => item.GroupName)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var average = group.Average(item => item.Availability30DaysPercent!.Value);
                return new MetricRowViewModel
                {
                    Title = group.Key,
                    Value = $"{average:0.0}%",
                    Percent = average,
                    AccentColor = average >= 99 ? "#16A34A" : average >= 95 ? "#EA580C" : "#DC2626"
                };
            });

        ReplaceCollection(GroupAvailabilityRows, rows);
    }

    private void ApplyGroupAvailabilityToGroups()
    {
        var availabilityByGroup = AvailabilityItems
            .Where(item => !string.IsNullOrWhiteSpace(item.GroupName) && item.Availability30DaysPercent.HasValue)
            .GroupBy(item => item.GroupName)
            .ToDictionary(
                group => group.Key,
                group => (double?)group.Average(item => item.Availability30DaysPercent!.Value),
                StringComparer.OrdinalIgnoreCase);

        foreach (var group in DeviceGroups)
        {
            group.Availability30DaysPercent = availabilityByGroup.TryGetValue(group.Name, out var availability)
                ? availability
                : null;
        }
    }

    private bool FilterDevice(object item)
    {
        if (item is not Device device)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(DeviceSearchText))
        {
            var search = DeviceSearchText.Trim();
            if (!Contains(device.Name, search)
                && !Contains(device.IpAddress, search)
                && !Contains(device.Location, search)
                && !Contains(device.GroupName, search)
                && !Contains(device.Description, search))
            {
                return false;
            }
        }

        var type = ParseDeviceTypeFilter(DeviceTypeFilter);
        if (type.HasValue && device.DeviceType != type.Value)
        {
            return false;
        }

        var status = ParseStatusFilter(DeviceStatusFilter);
        if (status.HasValue && device.LastStatus != status.Value)
        {
            return false;
        }

        if (DeviceGroupFilter != AllGroupsText && !string.Equals(device.GroupName, DeviceGroupFilter, StringComparison.OrdinalIgnoreCase))
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

        return true;
    }

    private void RefreshGroupOptions()
    {
        ReplaceCollection(DeviceGroupOptions, new[] { new SelectionOption<int?>(null, "Grupsuz") }
            .Concat(DeviceGroups.Select(group => new SelectionOption<int?>((int?)group.Id, group.Name))));

        DeviceGroupFilterOptions.Clear();
        DeviceGroupFilterOptions.Add(AllGroupsText);
        foreach (var groupName in DeviceGroups.Select(group => group.Name).OrderBy(name => name))
        {
            DeviceGroupFilterOptions.Add(groupName);
        }

        if (!DeviceGroupFilterOptions.Contains(DeviceGroupFilter))
        {
            DeviceGroupFilter = AllGroupsText;
        }

        if (!DeviceGroupFilterOptions.Contains(LogGroupFilter))
        {
            LogGroupFilter = AllGroupsText;
        }
    }

    private void RefreshSchedulePlanOptions()
    {
        ReplaceCollection(SchedulePlanOptions, new[] { new SelectionOption<int?>(null, "Plan yok") }
            .Concat(SchedulePlans.Select(plan => new SelectionOption<int?>((int?)plan.Id, plan.Name))));
    }

    private void UpdatePlanTargetOptions(bool keepCurrentValue = false)
    {
        var current = keepCurrentValue ? PlanFormTargetValue : string.Empty;
        var options = PlanFormTargetType switch
        {
            SchedulePlanTargetType.Device => Devices.Select(device => new SelectionOption<string>(device.Id.ToString(CultureInfo.InvariantCulture), $"{device.IpAddress} - {device.Name}")),
            SchedulePlanTargetType.DeviceType => DeviceTypeOptions.Select(option => new SelectionOption<string>(option.Value.ToStorageValue(), option.Label)),
            SchedulePlanTargetType.DeviceGroup => DeviceGroups.Select(group => new SelectionOption<string>(group.Id.ToString(CultureInfo.InvariantCulture), group.Name)),
            SchedulePlanTargetType.CriticalDevices => new[] { new SelectionOption<string>(string.Empty, "Kritik cihazlar") },
            SchedulePlanTargetType.AllDevices => new[] { new SelectionOption<string>(string.Empty, "Tüm cihazlar") },
            _ => new[] { new SelectionOption<string>(string.Empty, "Tüm cihazlar") }
        };

        ReplaceCollection(PlanTargetOptions, options);
        PlanFormTargetValue = PlanTargetOptions.Any(option => option.Value == current)
            ? current
            : PlanTargetOptions.FirstOrDefault()?.Value ?? string.Empty;
    }

    private IEnumerable<Device> ResolveTargetsForPlan(SchedulePlan plan, bool respectAutoCheck)
    {
        var activeDevices = Devices.Where(device => device.IsActive && (!respectAutoCheck || device.AutoCheckEnabled));
        return plan.TargetType switch
        {
            SchedulePlanTargetType.Device => activeDevices.Where(device => int.TryParse(plan.TargetValue, out var id) ? device.Id == id : device.IpAddress == plan.TargetValue),
            SchedulePlanTargetType.DeviceType => activeDevices.Where(device => device.DeviceType == DeviceTypeExtensions.FromStorageValue(plan.TargetValue)),
            SchedulePlanTargetType.DeviceGroup => activeDevices.Where(device => int.TryParse(plan.TargetValue, out var id) ? device.GroupId == id : device.GroupName == plan.TargetValue),
            SchedulePlanTargetType.CriticalDevices => activeDevices.Where(device => device.IsCritical),
            SchedulePlanTargetType.AllDevices => activeDevices,
            _ => activeDevices
        };
    }

    private string ResolvePlanTargetDisplayName(SchedulePlan plan)
    {
        return plan.TargetType switch
        {
            SchedulePlanTargetType.Device => Devices.FirstOrDefault(device => device.Id.ToString(CultureInfo.InvariantCulture) == plan.TargetValue) is { } device
                ? $"{device.IpAddress} - {device.Name}"
                : plan.TargetValue,
            SchedulePlanTargetType.DeviceType => DeviceTypeExtensions.FromStorageValue(plan.TargetValue).ToDisplayName(),
            SchedulePlanTargetType.DeviceGroup => DeviceGroups.FirstOrDefault(group => group.Id.ToString(CultureInfo.InvariantCulture) == plan.TargetValue)?.Name ?? plan.TargetValue,
            SchedulePlanTargetType.CriticalDevices => "Kritik cihazlar",
            SchedulePlanTargetType.AllDevices => "Tüm cihazlar",
            _ => plan.TargetValue
        };
    }

    private string ResolveGroupName(int? groupId)
    {
        return groupId.HasValue
            ? DeviceGroups.FirstOrDefault(group => group.Id == groupId.Value)?.Name ?? string.Empty
            : string.Empty;
    }

    private static int ConvertFrequencyToMinutes(int value, string unit)
    {
        var normalized = Math.Max(1, value);
        return unit switch
        {
            "Saat" => normalized * 60,
            "Gün" => normalized * 24 * 60,
            _ => normalized
        };
    }

    private void ApplyFrequency(int intervalMinutes)
    {
        if (intervalMinutes % (24 * 60) == 0)
        {
            PlanFormFrequencyValue = Math.Max(1, intervalMinutes / (24 * 60));
            PlanFormFrequencyUnit = "Gün";
            return;
        }

        if (intervalMinutes % 60 == 0)
        {
            PlanFormFrequencyValue = Math.Max(1, intervalMinutes / 60);
            PlanFormFrequencyUnit = "Saat";
            return;
        }

        PlanFormFrequencyValue = Math.Max(1, intervalMinutes);
        PlanFormFrequencyUnit = "Dakika";
    }

    private DeviceType? ParseDeviceTypeFilter(string value)
    {
        if (value == AllDeviceTypesText)
        {
            return null;
        }

        var option = DeviceTypeOptions.FirstOrDefault(item => item.Label == value);
        return option?.Value;
    }

    private static DeviceStatus? ParseStatusFilter(string value)
    {
        if (value == AllStatusesText)
        {
            return null;
        }

        return Enum.GetValues<DeviceStatus>().FirstOrDefault(status => status.ToDisplayName() == value);
    }

    private static PingTriggerType? ParseTriggerFilter(string value)
    {
        if (value == AllTriggersText)
        {
            return null;
        }

        return Enum.GetValues<PingTriggerType>().FirstOrDefault(trigger => trigger.ToDisplayName() == value);
    }

    private static bool Contains(string source, string search)
    {
        return source?.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0;
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }

    private void RaiseCommandStates()
    {
        SaveDeviceCommand?.NotifyCanExecuteChanged();
        ClearDeviceFormCommand?.NotifyCanExecuteChanged();
        EditSelectedDeviceCommand?.NotifyCanExecuteChanged();
        DeleteSelectedDeviceCommand?.NotifyCanExecuteChanged();
        PingAllCommand?.NotifyCanExecuteChanged();
        PingFilteredDevicesCommand?.NotifyCanExecuteChanged();
        PingSelectedDeviceCommand?.NotifyCanExecuteChanged();
        PingSelectedTypeCommand?.NotifyCanExecuteChanged();
        CancelPingCommand?.NotifyCanExecuteChanged();
        SaveGroupCommand?.NotifyCanExecuteChanged();
        ClearGroupFormCommand?.NotifyCanExecuteChanged();
        EditSelectedGroupCommand?.NotifyCanExecuteChanged();
        DeleteSelectedGroupCommand?.NotifyCanExecuteChanged();
        PingSelectedGroupCommand?.NotifyCanExecuteChanged();
        SaveSchedulePlanCommand?.NotifyCanExecuteChanged();
        ClearSchedulePlanFormCommand?.NotifyCanExecuteChanged();
        EditSelectedSchedulePlanCommand?.NotifyCanExecuteChanged();
        DeleteSelectedSchedulePlanCommand?.NotifyCanExecuteChanged();
        RunSelectedSchedulePlanCommand?.NotifyCanExecuteChanged();
        StartSchedulerCommand?.NotifyCanExecuteChanged();
        StopSchedulerCommand?.NotifyCanExecuteChanged();
        RefreshLogsCommand?.NotifyCanExecuteChanged();
        ClearLogsCommand?.NotifyCanExecuteChanged();
        ClearOldLogsCommand?.NotifyCanExecuteChanged();
        ExportLogsCommand?.NotifyCanExecuteChanged();
        RefreshAvailabilityCommand?.NotifyCanExecuteChanged();
        ExportAvailabilityCommand?.NotifyCanExecuteChanged();
        ExportDevicesCommand?.NotifyCanExecuteChanged();
        ImportDevicesCommand?.NotifyCanExecuteChanged();
        CreateCsvTemplateCommand?.NotifyCanExecuteChanged();
        SaveSettingsCommand?.NotifyCanExecuteChanged();
        ResetSettingsCommand?.NotifyCanExecuteChanged();
        BackupDatabaseCommand?.NotifyCanExecuteChanged();
        RestoreDatabaseCommand?.NotifyCanExecuteChanged();
        ExportSettingsCommand?.NotifyCanExecuteChanged();
        ImportSettingsCommand?.NotifyCanExecuteChanged();
    }
}
