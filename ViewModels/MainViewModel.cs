using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Infrastructure;
using NetworkHealthMonitor.Models;
using NetworkHealthMonitor.Services;

namespace NetworkHealthMonitor.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private const string SectionDashboard = "Dashboard";
    private const string SectionDevices = "Cihazlar";
    private const string SectionDeviceEdit = "Cihaz Ekle / Düzenle";
    private const string SectionGroups = "Cihaz Grupları";
    private const string SectionSchedules = "Otomatik Kontrol Planları";
    private const string SectionAvailability = "Ag Erisilebilirligi";
    private const string SectionLogs = "Ping Logları";
    private const string SectionNotifications = "Bildirim Kuyrugu";
    private const string SectionSettings = "Ayarlar";
    private const string AllDeviceTypesText = "Tüm Tipler";
    private const string AllStatusesText = "Tüm Durumlar";
    private const string AllGroupsText = "Tüm Gruplar";
    private const string AllTriggersText = "Tüm Kaynaklar";
    private const string AllCriticalText = "Tüm Cihazlar";
    private const string CriticalOnlyText = "Sadece kritik";
    private const string NonCriticalOnlyText = "Kritik olmayan";
    private const string ActiveDevicesText = "Aktif kayitlar";
    private const string DeletedDevicesText = "Silinen cihazlar";
    private const string AllDeletionStatesText = "Tum kayitlar";
    private const string AllOutboxStatusesText = "Tum durumlar";
    private const string AllOutboxEventTypesText = "Tum bildirimler";

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
    private readonly SchedulePlanTargetResolver _schedulePlanTargetResolver;
    private readonly CsvExportService _csvExportService;
    private readonly DeviceImportExportService _deviceImportExportService;
    private readonly IDeviceCheckPolicyService _deviceCheckPolicyService;
    private readonly AppSettingsService _settingsService;
    private readonly IDialogService _dialogService;
    private readonly DataMaintenanceService _maintenanceService;
    private readonly IWindowsServiceStatusService _windowsServiceStatusService;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly INtfyNotificationClient _ntfyNotificationClient;
    private readonly INotificationOutboxRepository _notificationOutboxRepository;
    private readonly WorkerHeartbeatRepository _workerHeartbeatRepository;
    private readonly IUiAutostartService _uiAutostartService;

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
    private string _deletedDeviceFilter = ActiveDevicesText;
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
    private int? _formPingTimeoutMs;
    private int _formCheckIntervalSeconds;
    private int _formFailureRetryIntervalSeconds;
    private int _formFailureRetryLimit;
    private int _formFailureThreshold;
    private bool _formIsCritical;
    private bool _formIsActive = true;
    private int? _editingGroupId;
    private string _groupFormName = string.Empty;
    private string _groupFormDescription = string.Empty;
    private int? _groupFormDefaultSchedulePlanId;
    private bool? _groupFormDefaultAutoCheckEnabled;
    private int? _groupFormDefaultCheckIntervalSeconds;
    private int? _groupFormDefaultPingTimeoutMs;
    private int? _groupFormDefaultFailureRetryIntervalSeconds;
    private int? _groupFormDefaultFailureRetryLimit;
    private int? _groupFormDefaultFailureThreshold;
    private int? _editingPlanId;
    private string _planFormName = string.Empty;
    private SchedulePlanTargetType _planFormTargetType = SchedulePlanTargetType.AllDevices;
    private string _planFormTargetValue = string.Empty;
    private int _planFormFrequencyValue = AppSettings.DefaultSchedulePlanIntervalMinutes;
    private string _planFormFrequencyUnit = "Dakika";
    private int _planFormTimeoutMs = AppSettings.DefaultPingTimeoutMs;
    private int _planFormMaxParallelism = AppSettings.DefaultSchedulePlanMaxParallelism;
    private int _planFormFailureThreshold = AppSettings.DefaultFailureThresholdValue;
    private bool _planFormIsActive = true;
    private string _planFormDescription = string.Empty;
    private int _pingTimeoutMs = AppSettings.DefaultPingTimeoutMs;
    private int _maxParallelPings = AppSettings.DefaultMaxParallelPings;
    private int _defaultFailureThreshold = AppSettings.DefaultFailureThresholdValue;
    private int _autoCheckIntervalMinutes = AppSettings.DefaultAutoCheckIntervalMinutes;
    private int _schedulerPollIntervalSeconds = AppSettings.DefaultSchedulerPollIntervalSeconds;
    private bool _autoCheckEnabled = true;
    private int _defaultFailureRetryIntervalSeconds = AppSettings.DefaultFailureRetryIntervalSecondsValue;
    private int _defaultFailureRetryLimit = AppSettings.DefaultFailureRetryLimitValue;
    private bool _startSchedulePlansOnStartup = true;
    private string _csvDelimiter = ";";
    private int _logRetentionDays = AppSettings.DefaultLogRetentionDays;
    private int _availabilityPeriodRetentionDays = AppSettings.DefaultAvailabilityPeriodRetentionDays;
    private int _incidentRetentionDays = AppSettings.DefaultIncidentRetentionDays;
    private int _dailyAggregateRetentionDays = AppSettings.DefaultDailyAggregateRetentionDays;
    private int _heartbeatGraceSeconds = AppSettings.DefaultHeartbeatGraceSeconds;
    private int _expectedCheckGraceMultiplier = AppSettings.DefaultExpectedCheckGraceMultiplier;
    private DowntimeStartPolicy _downtimeStartPolicy = DowntimeStartPolicy.FirstFailedCheck;
    private string _exportDirectory = string.Empty;
    private string _theme = "Açık";
    private bool _notificationEnabled;
    private string _notificationBaseUrl = "https://ntfy.sh";
    private string _notificationTopic = string.Empty;
    private string _notificationAccessToken = string.Empty;
    private bool _notificationIncludeIpAddress = true;
    private bool _notificationNotifyOnDown = true;
    private bool _notificationNotifyOnRecovered = true;
    private int _notificationDownFailureThreshold = 3;
    private int _notificationRecoverySuccessThreshold = 2;
    private int _notificationCooldownMinutes = 15;
    private int _notificationRequestTimeoutSeconds = 10;
    private int _notificationMaxRetryCount = 5;
    private int _notificationInitialRetryDelaySeconds = 30;
    private string _notificationLastTestResult = string.Empty;
    private string _notificationLastSuccessfulAtText = "-";
    private string _notificationLastError = string.Empty;
    private string _workerHealthText = "Bilinmiyor";
    private string _workerVersionText = "-";
    private string _workerStartedAtText = "-";
    private string _workerLastSeenAtText = "-";
    private string _workerLastSchedulerCycleText = "-";
    private string _workerLastScheduledPingText = "-";
    private string _workerLastNotificationText = "-";
    private int _pendingNotificationCount;
    private int _failedNotificationCount;
    private NotificationOutboxItem? _selectedNotificationOutboxItem;
    private string _outboxStatusFilter = AllOutboxStatusesText;
    private string _outboxEventTypeFilter = AllOutboxEventTypesText;
    private int? _outboxDeviceFilterId;
    private DateTime? _outboxStartDate;
    private DateTime? _outboxEndDate;
    private int? _bulkTargetGroupId;
    private int _bulkCheckIntervalSeconds;
    private bool _selectAllVisibleDevices;
    private CsvImportMode _csvImportMode = CsvImportMode.AddOnly;
    private CsvImportScope _csvImportScope = CsvImportScope.AllActiveDevices;
    private int? _csvImportGroupId;
    private string _csvImportFilePath = string.Empty;
    private string _csvImportPreviewSummary = "CSV onizlemesi henuz alinmadi.";
    private CsvImportPreview? _csvImportPreview;
    private bool _openUiOnWindowsLogin;
    private bool _deleteEmptyGroupAfterDeviceDelete;
    private int _pingTotalCount;
    private int _pingCompletedCount;
    private int _pingSuccessCount;
    private int _pingFailureCount;
    private bool _isSchedulerRunning;
    private string _schedulerStatusText = "Bilinmiyor";

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
        SchedulePlanTargetResolver schedulePlanTargetResolver,
        CsvExportService csvExportService,
        DeviceImportExportService deviceImportExportService,
        IDeviceCheckPolicyService deviceCheckPolicyService,
        AppSettingsService settingsService,
        IDialogService dialogService,
        DataMaintenanceService maintenanceService,
        IWindowsServiceStatusService? windowsServiceStatusService = null,
        INotificationPublisher? notificationPublisher = null,
        INtfyNotificationClient? ntfyNotificationClient = null,
        INotificationOutboxRepository? notificationOutboxRepository = null,
        WorkerHeartbeatRepository? workerHeartbeatRepository = null,
        IUiAutostartService? uiAutostartService = null)
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
        _schedulePlanTargetResolver = schedulePlanTargetResolver;
        _csvExportService = csvExportService;
        _deviceImportExportService = deviceImportExportService;
        _deviceCheckPolicyService = deviceCheckPolicyService;
        _settingsService = settingsService;
        _dialogService = dialogService;
        _maintenanceService = maintenanceService;
        _windowsServiceStatusService = windowsServiceStatusService ?? new WindowsServiceStatusService();
        _notificationOutboxRepository = notificationOutboxRepository ?? new NotificationOutboxRepository(deviceRepository.ConnectionFactory);
        _notificationPublisher = notificationPublisher ?? new NotificationPublisher(_notificationOutboxRepository);
        _ntfyNotificationClient = ntfyNotificationClient ?? new NtfyNotificationClient(new DefaultHttpClientFactory(), new DpapiSecretProtector());
        _workerHeartbeatRepository = workerHeartbeatRepository ?? new WorkerHeartbeatRepository(deviceRepository.ConnectionFactory);
        _uiAutostartService = uiAutostartService ?? new WindowsStartupShortcutService();
        _maintenanceWindowRepository = new MaintenanceWindowRepository(deviceRepository.ConnectionFactory);
        _monitoringCalendarRepository = new MonitoringCalendarRepository(deviceRepository.ConnectionFactory);
        _systemReadinessService = new SystemReadinessService(
            deviceRepository.ConnectionFactory,
            _workerHeartbeatRepository,
            _notificationOutboxRepository,
            _windowsServiceStatusService);

        DeviceTypeOptions = new ObservableCollection<DeviceTypeOption>(DeviceTypeOption.CreateAll());
        DeviceTypeFilterOptions = new ObservableCollection<string>(new[] { AllDeviceTypesText }.Concat(DeviceTypeOptions.Select(option => option.Label)));
        DeviceStatusFilterOptions = new ObservableCollection<string>(new[]
        {
            AllStatusesText,
            DeviceStatus.Online.ToDisplayName(),
            DeviceStatus.Warning.ToDisplayName(),
            DeviceStatus.UnderWatch.ToDisplayName(),
            DeviceStatus.Offline.ToDisplayName(),
            DeviceStatus.PingBlockedOrNoReply.ToDisplayName(),
            DeviceStatus.Unknown.ToDisplayName()
        });
        CriticalFilterOptions = new ObservableCollection<string>(new[] { AllCriticalText, CriticalOnlyText, NonCriticalOnlyText });
        DeviceDeletedFilterOptions = new ObservableCollection<string>(new[] { ActiveDevicesText, DeletedDevicesText, AllDeletionStatesText });
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
        DeviceTypePolicies = new ObservableCollection<DeviceTypePolicy>(DeviceTypePolicy.CreateDefaults());
        CsvImportModeOptions = new ObservableCollection<SelectionOption<CsvImportMode>>(new[]
        {
            new SelectionOption<CsvImportMode>(CsvImportMode.AddOnly, "Sadece yeni cihazlari ekle"),
            new SelectionOption<CsvImportMode>(CsvImportMode.Upsert, "Ekle veya guncelle"),
            new SelectionOption<CsvImportMode>(CsvImportMode.Sync, "CSV ile tamamen esitle")
        });
        CsvImportScopeOptions = new ObservableCollection<SelectionOption<CsvImportScope>>(new[]
        {
            new SelectionOption<CsvImportScope>(CsvImportScope.AllActiveDevices, "Tum aktif cihazlari CSV ile esitle"),
            new SelectionOption<CsvImportScope>(CsvImportScope.SelectedGroup, "Yalnizca secili grubu CSV ile esitle")
        });
        OutboxStatusOptions = new ObservableCollection<string>(new[] { AllOutboxStatusesText, "Pending", "Processing", "Sent", "Failed", "Cancelled" });
        OutboxEventTypeOptions = new ObservableCollection<string>(new[] { AllOutboxEventTypesText, "DeviceDown", "DeviceRecovered", "Test" });
        DowntimeStartPolicyOptions = new ObservableCollection<SelectionOption<DowntimeStartPolicy>>(new[]
        {
            new SelectionOption<DowntimeStartPolicy>(DowntimeStartPolicy.FirstFailedCheck, "Ilk basarisiz kontrol"),
            new SelectionOption<DowntimeStartPolicy>(DowntimeStartPolicy.ConfirmedDownTime, "Down dogrulama zamani")
        });

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
        NavigateMaintenanceCommand = new RelayCommand(() => CurrentSection = SectionMaintenance);
        NavigateCalendarsCommand = new RelayCommand(() => CurrentSection = SectionCalendars);
        NavigateReadinessCommand = new RelayCommand(() => CurrentSection = SectionReadiness);
        NavigateLogsCommand = new RelayCommand(() => CurrentSection = SectionLogs);
        NavigateNotificationsCommand = new RelayCommand(() => CurrentSection = SectionNotifications);
        NavigateSettingsCommand = new RelayCommand(() => CurrentSection = SectionSettings);

        SaveDeviceCommand = new AsyncRelayCommand(SaveDeviceAsync, () => !IsBusy);
        ClearDeviceFormCommand = new RelayCommand(ClearDeviceForm, () => !IsBusy);
        EditSelectedDeviceCommand = new RelayCommand(() => StartEditDevice(SelectedDevice), () => SelectedDevice is not null && !IsBusy);
        EditDeviceCommand = new RelayCommand<Device>(StartEditDevice, device => device is not null && !IsBusy);
        DeleteSelectedDeviceCommand = new AsyncRelayCommand(() => DeleteDeviceAsync(SelectedDevice), () => SelectedDevice is { IsDeleted: false } && !IsBusy);
        DeleteDeviceCommand = new AsyncRelayCommand<Device>(DeleteDeviceAsync, device => device is { IsDeleted: false } && !IsBusy);
        RestoreSelectedDeviceCommand = new AsyncRelayCommand(() => RestoreDeviceAsync(SelectedDevice), () => SelectedDevice is { IsDeleted: true } && !IsBusy);
        RestoreDeviceCommand = new AsyncRelayCommand<Device>(RestoreDeviceAsync, device => device is { IsDeleted: true } && !IsBusy);

        PingAllCommand = new AsyncRelayCommand(() => RunManualPingAsync(Devices.ToList(), PingTriggerType.Manual), () => Devices.Count > 0 && !IsBusy);
        PingFilteredDevicesCommand = new AsyncRelayCommand(() => RunManualPingAsync(DevicesView.Cast<Device>().ToList(), PingTriggerType.Manual), () => DevicesView.Cast<Device>().Any() && !IsBusy);
        PingSelectedDeviceCommand = new AsyncRelayCommand(() => SelectedDevice is null ? Task.CompletedTask : RunManualPingAsync(new[] { SelectedDevice }, PingTriggerType.SelectedDeviceManual), () => SelectedDevice is not null && !IsBusy);
        PingDeviceCommand = new AsyncRelayCommand<Device>(device => device is null ? Task.CompletedTask : RunManualPingAsync(new[] { device }, PingTriggerType.SelectedDeviceManual), device => device is not null && !IsBusy);
        PingSelectedTypeCommand = new AsyncRelayCommand(PingSelectedTypeAsync, () => DeviceTypeFilter != AllDeviceTypesText && !IsBusy);
        PingSelectedDevicesBulkCommand = new AsyncRelayCommand<object>(parameter => RunManualPingAsync(GetSelectedDevices(parameter), PingTriggerType.SelectedDeviceManual), CanUseSelectedDevices);
        EnableAutoCheckSelectedCommand = new AsyncRelayCommand<object>(parameter => BulkSetAutoCheckAsync(parameter, true), CanUseSelectedDevices);
        DisableAutoCheckSelectedCommand = new AsyncRelayCommand<object>(parameter => BulkSetAutoCheckAsync(parameter, false), CanUseSelectedDevices);
        AssignSelectedDevicesToGroupCommand = new AsyncRelayCommand<object>(BulkAssignGroupAsync, CanUseSelectedDevices);
        ApplySelectedCheckIntervalCommand = new AsyncRelayCommand<object>(BulkApplyCheckIntervalAsync, CanUseSelectedDevices);
        DeactivateSelectedDevicesCommand = new AsyncRelayCommand<object>(BulkDeactivateAsync, CanUseSelectedDevices);
        DeleteSelectedDevicesBulkCommand = new AsyncRelayCommand<object>(BulkDeleteAsync, CanUseSelectedDevices);
        RestoreSelectedDevicesBulkCommand = new AsyncRelayCommand<object>(BulkRestoreAsync, parameter => !IsBusy && GetSelectedDevices(parameter).Any(device => device.IsDeleted));
        ToggleAllVisibleDevicesSelectionCommand = new RelayCommand(ToggleAllVisibleDevicesSelection, () => !IsBusy);
        CancelPingCommand = new RelayCommand(CancelPing, () => IsPinging);

        SaveGroupCommand = new AsyncRelayCommand(SaveGroupAsync, () => !IsBusy);
        ClearGroupFormCommand = new RelayCommand(ClearGroupForm, () => !IsBusy);
        EditSelectedGroupCommand = new RelayCommand(() => StartEditGroup(SelectedGroup), () => SelectedGroup is not null && !IsBusy);
        DeleteSelectedGroupCommand = new AsyncRelayCommand(() => DeleteGroupAsync(SelectedGroup), () => SelectedGroup is not null && !IsBusy);
        DeleteSelectedGroupDevicesCommand = new AsyncRelayCommand(() => DeleteGroupDevicesAsync(SelectedGroup), () => SelectedGroup is not null && !IsBusy);
        PingSelectedGroupCommand = new AsyncRelayCommand(() => SelectedGroup is null ? Task.CompletedTask : PingGroupAsync(SelectedGroup), () => SelectedGroup is not null && !IsBusy);

        SaveSchedulePlanCommand = new AsyncRelayCommand(SaveSchedulePlanAsync, () => !IsBusy);
        ClearSchedulePlanFormCommand = new RelayCommand(ClearSchedulePlanForm, () => !IsBusy);
        EditSelectedSchedulePlanCommand = new RelayCommand(() => StartEditSchedulePlan(SelectedSchedulePlan), () => SelectedSchedulePlan is not null && !IsBusy);
        DeleteSelectedSchedulePlanCommand = new AsyncRelayCommand(() => DeleteSchedulePlanAsync(SelectedSchedulePlan), () => SelectedSchedulePlan is not null && !IsBusy);
        RunSelectedSchedulePlanCommand = new AsyncRelayCommand(() => SelectedSchedulePlan is null ? Task.CompletedTask : RunSchedulePlanNowAsync(SelectedSchedulePlan), () => SelectedSchedulePlan is { IsActive: true } && !IsBusy);
        StartSchedulerCommand = new AsyncRelayCommand(ShowServiceControlInfoAsync, () => !IsBusy);
        StopSchedulerCommand = new AsyncRelayCommand(ShowServiceControlInfoAsync, () => !IsBusy);

        RefreshLogsCommand = new AsyncRelayCommand(LoadLogsAsync, () => !IsBusy);
        ClearLogsCommand = new AsyncRelayCommand(ClearLogsAsync, () => Logs.Count > 0 && !IsBusy);
        ClearOldLogsCommand = new AsyncRelayCommand(ClearOldLogsAsync, () => !IsBusy && LogRetentionDays > 0);
        ExportLogsCommand = new AsyncRelayCommand(ExportLogsAsync, () => Logs.Count > 0 && !IsBusy);
        RefreshAvailabilityCommand = new AsyncRelayCommand(LoadAvailabilityAsync, () => !IsBusy);
        ExportAvailabilityCommand = new AsyncRelayCommand(ExportAvailabilityAsync, () => AvailabilityItems.Count > 0 && !IsBusy);
        ExportAvailabilityIncidentsCommand = new AsyncRelayCommand(ExportAvailabilityIncidentsAsync, () => !IsBusy);
        RecalculateAvailabilityCommand = new AsyncRelayCommand(RecalculateAvailabilityAsync, () => !IsBusy);
        RefreshSelectedDeviceTimelineCommand = new AsyncRelayCommand(RefreshSelectedDeviceAvailabilityAsync, () => SelectedDevice is not null && !IsBusy);
        SaveMaintenanceWindowCommand = new AsyncRelayCommand(SaveMaintenanceWindowAsync, () => !IsBusy);
        ClearMaintenanceWindowFormCommand = new RelayCommand(ClearMaintenanceWindowForm, () => !IsBusy);
        EditSelectedMaintenanceWindowCommand = new RelayCommand(StartEditMaintenanceWindow, () => SelectedMaintenanceWindow is not null && !IsBusy);
        CancelSelectedMaintenanceWindowCommand = new AsyncRelayCommand(CancelSelectedMaintenanceWindowAsync, () => SelectedMaintenanceWindow is not null && !IsBusy);
        CompleteSelectedMaintenanceWindowCommand = new AsyncRelayCommand(CompleteSelectedMaintenanceWindowAsync, () => SelectedMaintenanceWindow is not null && !IsBusy);
        SaveMonitoringCalendarCommand = new AsyncRelayCommand(SaveMonitoringCalendarAsync, () => !IsBusy);
        ClearMonitoringCalendarFormCommand = new RelayCommand(ClearMonitoringCalendarForm, () => !IsBusy);
        EditSelectedMonitoringCalendarCommand = new RelayCommand(StartEditMonitoringCalendar, () => SelectedMonitoringCalendar is not null && !IsBusy);
        DeleteSelectedMonitoringCalendarCommand = new AsyncRelayCommand(DeleteSelectedMonitoringCalendarAsync, () => SelectedMonitoringCalendar is not null && !IsBusy);
        RefreshReadinessCommand = new AsyncRelayCommand(RefreshReadinessAsync, () => !IsBusy);
        ExportDevicesCommand = new AsyncRelayCommand(ExportDevicesAsync, () => Devices.Count > 0 && !IsBusy);
        ImportDevicesCommand = new AsyncRelayCommand(PreviewImportDevicesAsync, () => !IsBusy);
        ApplyCsvImportCommand = new AsyncRelayCommand(ApplyCsvImportAsync, () => !IsBusy && CsvImportCanApply);
        CreateCsvTemplateCommand = new AsyncRelayCommand(CreateCsvTemplateAsync, () => !IsBusy);
        RefreshOutboxCommand = new AsyncRelayCommand(LoadOutboxAsync, () => !IsBusy);
        RetrySelectedOutboxCommand = new AsyncRelayCommand<object>(RetrySelectedOutboxAsync, parameter => !IsBusy && GetSelectedOutboxItems(parameter).Any(item => item.Status == "Failed"));
        RetryOutboxCommand = new AsyncRelayCommand<NotificationOutboxItem>(item => RetryOutboxAsync(item), item => item?.Status == "Failed" && !IsBusy);
        CancelPendingOutboxCommand = new AsyncRelayCommand<NotificationOutboxItem>(item => CancelPendingOutboxAsync(item), item => item?.Status == "Pending" && !IsBusy);
        ShowOutboxDetailCommand = new RelayCommand<NotificationOutboxItem>(ShowOutboxDetail, item => item is not null);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, () => !IsBusy);
        ResetSettingsCommand = new AsyncRelayCommand(ResetSettingsAsync, () => !IsBusy);
        BackupDatabaseCommand = new AsyncRelayCommand(BackupDatabaseAsync, () => !IsBusy);
        RestoreDatabaseCommand = new AsyncRelayCommand(RestoreDatabaseAsync, () => !IsBusy);
        OptimizeDatabaseCommand = new AsyncRelayCommand(OptimizeDatabaseAsync, () => !IsBusy);
        ExportSettingsCommand = new AsyncRelayCommand(ExportSettingsAsync, () => !IsBusy);
        ImportSettingsCommand = new AsyncRelayCommand(ImportSettingsAsync, () => !IsBusy);
        SendTestNotificationCommand = new AsyncRelayCommand(SendTestNotificationAsync, () => !IsBusy);
        OpenLogFolderCommand = new RelayCommand(OpenLogFolder);

        _schedulerService.StatusChanged += SchedulerStatusChanged;
        EnsureSummaryCards();
        UpdatePlanTargetOptions();
    }

    public ObservableCollection<Device> Devices { get; } = new();

    public ObservableCollection<DeviceGroup> DeviceGroups { get; } = new();

    public ObservableCollection<SchedulePlan> SchedulePlans { get; } = new();

    public ObservableCollection<PingLog> Logs { get; } = new();

    public ObservableCollection<NotificationOutboxItem> NotificationOutboxItems { get; } = new();

    public ObservableCollection<CsvImportPreviewRow> CsvNewRows { get; } = new();

    public ObservableCollection<CsvImportPreviewRow> CsvUpdateRows { get; } = new();

    public ObservableCollection<CsvImportPreviewRow> CsvDeleteRows { get; } = new();

    public ObservableCollection<CsvImportPreviewRow> CsvInvalidRows { get; } = new();

    public ObservableCollection<CsvImportPreviewRow> CsvDuplicateRows { get; } = new();

    public ObservableCollection<CsvImportPreviewRow> CsvUnchangedRows { get; } = new();

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

    public ObservableCollection<string> DeviceDeletedFilterOptions { get; }

    public ObservableCollection<string> TriggerFilterOptions { get; }

    public ObservableCollection<SelectionOption<int?>> DeviceGroupOptions { get; } = new();

    public ObservableCollection<SelectionOption<int?>> DeviceOptions { get; } = new();

    public ObservableCollection<SelectionOption<int?>> SchedulePlanOptions { get; } = new();

    public ObservableCollection<SelectionOption<SchedulePlanTargetType>> PlanTargetTypeOptions { get; }

    public ObservableCollection<SelectionOption<string>> PlanTargetOptions { get; } = new();

    public ObservableCollection<string> FrequencyUnitOptions { get; }

    public ObservableCollection<string> ThemeOptions { get; }

    public ObservableCollection<DeviceTypePolicy> DeviceTypePolicies { get; }

    public ObservableCollection<SelectionOption<CsvImportMode>> CsvImportModeOptions { get; }

    public ObservableCollection<SelectionOption<CsvImportScope>> CsvImportScopeOptions { get; }

    public ObservableCollection<SelectionOption<DowntimeStartPolicy>> DowntimeStartPolicyOptions { get; }

    public ObservableCollection<string> OutboxStatusOptions { get; }

    public ObservableCollection<string> OutboxEventTypeOptions { get; }

    public ICollectionView DevicesView { get; }

    public ICollectionView LogsView { get; }

    public RelayCommand NavigateDashboardCommand { get; }
    public RelayCommand NavigateDevicesCommand { get; }
    public RelayCommand NavigateDeviceEditCommand { get; }
    public RelayCommand NavigateGroupsCommand { get; }
    public RelayCommand NavigateSchedulesCommand { get; }
    public RelayCommand NavigateAvailabilityCommand { get; }
    public RelayCommand NavigateLogsCommand { get; }
    public RelayCommand NavigateNotificationsCommand { get; }
    public RelayCommand NavigateSettingsCommand { get; }
    public AsyncRelayCommand SaveDeviceCommand { get; }
    public RelayCommand ClearDeviceFormCommand { get; }
    public RelayCommand EditSelectedDeviceCommand { get; }
    public RelayCommand<Device> EditDeviceCommand { get; }
    public AsyncRelayCommand DeleteSelectedDeviceCommand { get; }
    public AsyncRelayCommand<Device> DeleteDeviceCommand { get; }
    public AsyncRelayCommand RestoreSelectedDeviceCommand { get; }
    public AsyncRelayCommand<Device> RestoreDeviceCommand { get; }
    public AsyncRelayCommand PingAllCommand { get; }
    public AsyncRelayCommand PingFilteredDevicesCommand { get; }
    public AsyncRelayCommand PingSelectedDeviceCommand { get; }
    public AsyncRelayCommand<Device> PingDeviceCommand { get; }
    public AsyncRelayCommand PingSelectedTypeCommand { get; }
    public AsyncRelayCommand<object> PingSelectedDevicesBulkCommand { get; }
    public AsyncRelayCommand<object> EnableAutoCheckSelectedCommand { get; }
    public AsyncRelayCommand<object> DisableAutoCheckSelectedCommand { get; }
    public AsyncRelayCommand<object> AssignSelectedDevicesToGroupCommand { get; }
    public AsyncRelayCommand<object> ApplySelectedCheckIntervalCommand { get; }
    public AsyncRelayCommand<object> DeactivateSelectedDevicesCommand { get; }
    public AsyncRelayCommand<object> DeleteSelectedDevicesBulkCommand { get; }
    public AsyncRelayCommand<object> RestoreSelectedDevicesBulkCommand { get; }
    public RelayCommand ToggleAllVisibleDevicesSelectionCommand { get; }
    public RelayCommand CancelPingCommand { get; }
    public AsyncRelayCommand SaveGroupCommand { get; }
    public RelayCommand ClearGroupFormCommand { get; }
    public RelayCommand EditSelectedGroupCommand { get; }
    public AsyncRelayCommand DeleteSelectedGroupCommand { get; }
    public AsyncRelayCommand DeleteSelectedGroupDevicesCommand { get; }
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
    public AsyncRelayCommand ExportAvailabilityIncidentsCommand { get; }
    public AsyncRelayCommand RecalculateAvailabilityCommand { get; }
    public AsyncRelayCommand ExportDevicesCommand { get; }
    public AsyncRelayCommand ImportDevicesCommand { get; }
    public AsyncRelayCommand ApplyCsvImportCommand { get; }
    public AsyncRelayCommand CreateCsvTemplateCommand { get; }
    public AsyncRelayCommand RefreshOutboxCommand { get; }
    public AsyncRelayCommand<object> RetrySelectedOutboxCommand { get; }
    public AsyncRelayCommand<NotificationOutboxItem> RetryOutboxCommand { get; }
    public AsyncRelayCommand<NotificationOutboxItem> CancelPendingOutboxCommand { get; }
    public RelayCommand<NotificationOutboxItem> ShowOutboxDetailCommand { get; }
    public AsyncRelayCommand SaveSettingsCommand { get; }
    public AsyncRelayCommand ResetSettingsCommand { get; }
    public AsyncRelayCommand BackupDatabaseCommand { get; }
    public AsyncRelayCommand RestoreDatabaseCommand { get; }
    public AsyncRelayCommand OptimizeDatabaseCommand { get; }
    public AsyncRelayCommand ExportSettingsCommand { get; }
    public AsyncRelayCommand ImportSettingsCommand { get; }
    public AsyncRelayCommand SendTestNotificationCommand { get; }
    public RelayCommand OpenLogFolderCommand { get; }

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
                OnPropertyChanged(nameof(IsMaintenanceSection));
                OnPropertyChanged(nameof(IsCalendarsSection));
                OnPropertyChanged(nameof(IsReadinessSection));
                OnPropertyChanged(nameof(IsLogsSection));
                OnPropertyChanged(nameof(IsNotificationsSection));
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
        SectionAvailability => "Ping tabanli ag erisilebilirligi, kesinti, Unknown ve coverage metriklerini takip edin.",
        SectionMaintenance => "Planli bakim pencerelerini ve hedeflerini yonetin.",
        SectionCalendars => "Izleme takvimleri, gun/saat kurallari ve cihaz/grup atamalarini yonetin.",
        SectionReadiness => "Windows Service, heartbeat, outbox, disk ve diagnostics durumunu dogrulayin.",
        SectionLogs => "Ping geçmişini filtreleyin, temizleyin ve CSV olarak dışa aktarın.",
        SectionSettings => "Genel uygulama davranışlarını ve veri bakımını yönetin.",
        SectionNotifications => "Failed ve pending bildirim outbox kayitlarini yonetin.",
        _ => string.Empty
    };

    public bool IsDashboardSection => CurrentSection == SectionDashboard;
    public bool IsDevicesSection => CurrentSection == SectionDevices;
    public bool IsDeviceEditSection => CurrentSection == SectionDeviceEdit;
    public bool IsGroupsSection => CurrentSection == SectionGroups;
    public bool IsSchedulesSection => CurrentSection == SectionSchedules;
    public bool IsAvailabilitySection => CurrentSection == SectionAvailability;
    public bool IsLogsSection => CurrentSection == SectionLogs;
    public bool IsNotificationsSection => CurrentSection == SectionNotifications;
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
                _ = RefreshSelectedDeviceAvailabilityAsync();
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

    public string DeletedDeviceFilter
    {
        get => _deletedDeviceFilter;
        set
        {
            if (SetProperty(ref _deletedDeviceFilter, value ?? ActiveDevicesText))
            {
                DevicesView.Refresh();
                RaiseCommandStates();
            }
        }
    }

    public NotificationOutboxItem? SelectedNotificationOutboxItem
    {
        get => _selectedNotificationOutboxItem;
        set
        {
            if (SetProperty(ref _selectedNotificationOutboxItem, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string OutboxStatusFilter
    {
        get => _outboxStatusFilter;
        set => SetProperty(ref _outboxStatusFilter, value ?? AllOutboxStatusesText);
    }

    public string OutboxEventTypeFilter
    {
        get => _outboxEventTypeFilter;
        set => SetProperty(ref _outboxEventTypeFilter, value ?? AllOutboxEventTypesText);
    }

    public int? OutboxDeviceFilterId
    {
        get => _outboxDeviceFilterId;
        set => SetProperty(ref _outboxDeviceFilterId, value);
    }

    public DateTime? OutboxStartDate
    {
        get => _outboxStartDate;
        set => SetProperty(ref _outboxStartDate, value);
    }

    public DateTime? OutboxEndDate
    {
        get => _outboxEndDate;
        set => SetProperty(ref _outboxEndDate, value);
    }

    public CsvImportMode CsvImportMode
    {
        get => _csvImportMode;
        set
        {
            if (SetProperty(ref _csvImportMode, value))
            {
                ClearCsvPreview();
                OnPropertyChanged(nameof(IsCsvSyncMode));
            }
        }
    }

    public CsvImportScope CsvImportScope
    {
        get => _csvImportScope;
        set
        {
            if (SetProperty(ref _csvImportScope, value))
            {
                ClearCsvPreview();
                OnPropertyChanged(nameof(IsCsvGroupScope));
            }
        }
    }

    public int? CsvImportGroupId
    {
        get => _csvImportGroupId;
        set
        {
            if (SetProperty(ref _csvImportGroupId, value))
            {
                ClearCsvPreview();
            }
        }
    }

    public string CsvImportFilePath
    {
        get => _csvImportFilePath;
        private set => SetProperty(ref _csvImportFilePath, value ?? string.Empty);
    }

    public string CsvImportPreviewSummary
    {
        get => _csvImportPreviewSummary;
        private set => SetProperty(ref _csvImportPreviewSummary, value ?? string.Empty);
    }

    public bool CsvImportCanApply => _csvImportPreview is not null && _csvImportPreview.HasImportableRows;

    public bool IsCsvSyncMode => CsvImportMode == NetworkHealthMonitor.Models.CsvImportMode.Sync;

    public bool IsCsvGroupScope => CsvImportScope == NetworkHealthMonitor.Models.CsvImportScope.SelectedGroup;

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

    public int? FormPingTimeoutMs
    {
        get => _formPingTimeoutMs;
        set => SetProperty(
            ref _formPingTimeoutMs,
            value.HasValue && value.Value > 0
                ? Math.Clamp(value.Value, AppSettings.MinPingTimeoutMs, AppSettings.MaxPingTimeoutMs)
                : null);
    }

    public int FormCheckIntervalSeconds
    {
        get => _formCheckIntervalSeconds;
        set => SetProperty(
            ref _formCheckIntervalSeconds,
            value <= 0 ? 0 : Math.Clamp(value, AppSettings.MinDeviceCheckIntervalSeconds, AppSettings.MaxDeviceCheckIntervalSeconds));
    }

    public int FormFailureRetryIntervalSeconds
    {
        get => _formFailureRetryIntervalSeconds;
        set => SetProperty(
            ref _formFailureRetryIntervalSeconds,
            value <= 0 ? 0 : Math.Clamp(value, AppSettings.MinFailureRetryIntervalSeconds, AppSettings.MaxFailureRetryIntervalSeconds));
    }

    public int FormFailureRetryLimit
    {
        get => _formFailureRetryLimit;
        set => SetProperty(
            ref _formFailureRetryLimit,
            value <= 0 ? 0 : Math.Clamp(value, AppSettings.MinFailureRetryLimit, AppSettings.MaxFailureRetryLimit));
    }

    public int FormFailureThreshold
    {
        get => _formFailureThreshold;
        set => SetProperty(
            ref _formFailureThreshold,
            value <= 0 ? 0 : Math.Clamp(value, AppSettings.MinFailureThreshold, AppSettings.MaxFailureThreshold));
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

    public bool? GroupFormDefaultAutoCheckEnabled
    {
        get => _groupFormDefaultAutoCheckEnabled;
        set => SetProperty(ref _groupFormDefaultAutoCheckEnabled, value);
    }

    public int? GroupFormDefaultCheckIntervalSeconds
    {
        get => _groupFormDefaultCheckIntervalSeconds;
        set => SetProperty(
            ref _groupFormDefaultCheckIntervalSeconds,
            value.HasValue
                ? Math.Clamp(value.Value, AppSettings.MinDeviceCheckIntervalSeconds, AppSettings.MaxDeviceCheckIntervalSeconds)
                : null);
    }

    public int? GroupFormDefaultPingTimeoutMs
    {
        get => _groupFormDefaultPingTimeoutMs;
        set => SetProperty(
            ref _groupFormDefaultPingTimeoutMs,
            value.HasValue && value.Value > 0
                ? Math.Clamp(value.Value, AppSettings.MinPingTimeoutMs, AppSettings.MaxPingTimeoutMs)
                : null);
    }

    public int? GroupFormDefaultFailureRetryIntervalSeconds
    {
        get => _groupFormDefaultFailureRetryIntervalSeconds;
        set => SetProperty(
            ref _groupFormDefaultFailureRetryIntervalSeconds,
            value.HasValue && value.Value > 0
                ? Math.Clamp(value.Value, AppSettings.MinFailureRetryIntervalSeconds, AppSettings.MaxFailureRetryIntervalSeconds)
                : null);
    }

    public int? GroupFormDefaultFailureRetryLimit
    {
        get => _groupFormDefaultFailureRetryLimit;
        set => SetProperty(
            ref _groupFormDefaultFailureRetryLimit,
            value.HasValue && value.Value > 0
                ? Math.Clamp(value.Value, AppSettings.MinFailureRetryLimit, AppSettings.MaxFailureRetryLimit)
                : null);
    }

    public int? GroupFormDefaultFailureThreshold
    {
        get => _groupFormDefaultFailureThreshold;
        set => SetProperty(
            ref _groupFormDefaultFailureThreshold,
            value.HasValue && value.Value > 0
                ? Math.Clamp(value.Value, AppSettings.MinFailureThreshold, AppSettings.MaxFailureThreshold)
                : null);
    }

    public string GroupFormTitle => _editingGroupId.HasValue ? "Grubu Düzenle" : "Yeni Grup";

    public string GroupFormActionText => _editingGroupId.HasValue ? "Güncelle" : "Kaydet";

    public bool DeleteEmptyGroupAfterDeviceDelete
    {
        get => _deleteEmptyGroupAfterDeviceDelete;
        set => SetProperty(ref _deleteEmptyGroupAfterDeviceDelete, value);
    }

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

    public int AutoCheckIntervalMinutes
    {
        get => _autoCheckIntervalMinutes;
        set => SetProperty(ref _autoCheckIntervalMinutes, Math.Max(AppSettings.MinAutoCheckIntervalMinutes, value));
    }

    public int SchedulerPollIntervalSeconds
    {
        get => _schedulerPollIntervalSeconds;
        set => SetProperty(ref _schedulerPollIntervalSeconds, Math.Clamp(value, AppSettings.MinSchedulerPollIntervalSeconds, AppSettings.MaxSchedulerPollIntervalSeconds));
    }

    public bool AutoCheckEnabled
    {
        get => _autoCheckEnabled;
        set => SetProperty(ref _autoCheckEnabled, value);
    }

    public int DefaultFailureRetryIntervalSeconds
    {
        get => _defaultFailureRetryIntervalSeconds;
        set => SetProperty(ref _defaultFailureRetryIntervalSeconds, Math.Clamp(value, AppSettings.MinFailureRetryIntervalSeconds, AppSettings.MaxFailureRetryIntervalSeconds));
    }

    public int DefaultFailureRetryLimit
    {
        get => _defaultFailureRetryLimit;
        set => SetProperty(ref _defaultFailureRetryLimit, Math.Clamp(value, AppSettings.MinFailureRetryLimit, AppSettings.MaxFailureRetryLimit));
    }

    public bool StartSchedulePlansOnStartup
    {
        get => _startSchedulePlansOnStartup;
        set => SetProperty(ref _startSchedulePlansOnStartup, value);
    }

    public bool OpenUiOnWindowsLogin
    {
        get => _openUiOnWindowsLogin;
        set => SetProperty(ref _openUiOnWindowsLogin, value);
    }

    public string CsvDelimiter
    {
        get => _csvDelimiter;
        set => SetProperty(ref _csvDelimiter, value ?? ";");
    }

    public int LogRetentionDays
    {
        get => _logRetentionDays;
        set
        {
            if (SetProperty(ref _logRetentionDays, value))
            {
                ClearOldLogsCommand?.NotifyCanExecuteChanged();
            }
        }
    }

    public string ExportDirectory
    {
        get => _exportDirectory;
        set => SetProperty(ref _exportDirectory, value ?? string.Empty);
    }

    public string Theme
    {
        get => _theme;
        set => SetProperty(ref _theme, value ?? "Açık");
    }

    public bool NotificationEnabled
    {
        get => _notificationEnabled;
        set => SetProperty(ref _notificationEnabled, value);
    }

    public string NotificationBaseUrl
    {
        get => _notificationBaseUrl;
        set => SetProperty(ref _notificationBaseUrl, value ?? string.Empty);
    }

    public string NotificationTopic
    {
        get => _notificationTopic;
        set => SetProperty(ref _notificationTopic, value ?? string.Empty);
    }

    public string NotificationAccessToken
    {
        get => _notificationAccessToken;
        set => SetProperty(ref _notificationAccessToken, value ?? string.Empty);
    }

    public bool NotificationIncludeIpAddress
    {
        get => _notificationIncludeIpAddress;
        set => SetProperty(ref _notificationIncludeIpAddress, value);
    }

    public bool NotificationNotifyOnDown
    {
        get => _notificationNotifyOnDown;
        set => SetProperty(ref _notificationNotifyOnDown, value);
    }

    public bool NotificationNotifyOnRecovered
    {
        get => _notificationNotifyOnRecovered;
        set => SetProperty(ref _notificationNotifyOnRecovered, value);
    }

    public int NotificationDownFailureThreshold
    {
        get => _notificationDownFailureThreshold;
        set => SetProperty(ref _notificationDownFailureThreshold, Math.Clamp(value, 1, 20));
    }

    public int NotificationRecoverySuccessThreshold
    {
        get => _notificationRecoverySuccessThreshold;
        set => SetProperty(ref _notificationRecoverySuccessThreshold, Math.Clamp(value, 1, 20));
    }

    public int NotificationCooldownMinutes
    {
        get => _notificationCooldownMinutes;
        set => SetProperty(ref _notificationCooldownMinutes, Math.Clamp(value, 0, 1440));
    }

    public int NotificationRequestTimeoutSeconds
    {
        get => _notificationRequestTimeoutSeconds;
        set => SetProperty(ref _notificationRequestTimeoutSeconds, Math.Clamp(value, 1, 120));
    }

    public int NotificationMaxRetryCount
    {
        get => _notificationMaxRetryCount;
        set => SetProperty(ref _notificationMaxRetryCount, Math.Clamp(value, 0, 20));
    }

    public int NotificationInitialRetryDelaySeconds
    {
        get => _notificationInitialRetryDelaySeconds;
        set => SetProperty(ref _notificationInitialRetryDelaySeconds, Math.Clamp(value, 1, 3600));
    }

    public string NotificationLastTestResult
    {
        get => _notificationLastTestResult;
        private set => SetProperty(ref _notificationLastTestResult, value ?? string.Empty);
    }

    public string NotificationLastSuccessfulAtText
    {
        get => _notificationLastSuccessfulAtText;
        private set => SetProperty(ref _notificationLastSuccessfulAtText, value ?? "-");
    }

    public string NotificationLastError
    {
        get => _notificationLastError;
        private set => SetProperty(ref _notificationLastError, value ?? string.Empty);
    }

    public string WorkerHealthText
    {
        get => _workerHealthText;
        private set => SetProperty(ref _workerHealthText, value ?? "Bilinmiyor");
    }

    public string WorkerVersionText
    {
        get => _workerVersionText;
        private set => SetProperty(ref _workerVersionText, value ?? "-");
    }

    public string WorkerStartedAtText
    {
        get => _workerStartedAtText;
        private set => SetProperty(ref _workerStartedAtText, value ?? "-");
    }

    public string WorkerLastSeenAtText
    {
        get => _workerLastSeenAtText;
        private set => SetProperty(ref _workerLastSeenAtText, value ?? "-");
    }

    public string WorkerLastSchedulerCycleText
    {
        get => _workerLastSchedulerCycleText;
        private set => SetProperty(ref _workerLastSchedulerCycleText, value ?? "-");
    }

    public string WorkerLastScheduledPingText
    {
        get => _workerLastScheduledPingText;
        private set => SetProperty(ref _workerLastScheduledPingText, value ?? "-");
    }

    public string WorkerLastNotificationText
    {
        get => _workerLastNotificationText;
        private set => SetProperty(ref _workerLastNotificationText, value ?? "-");
    }

    public int PendingNotificationCount
    {
        get => _pendingNotificationCount;
        private set => SetProperty(ref _pendingNotificationCount, value);
    }

    public int FailedNotificationCount
    {
        get => _failedNotificationCount;
        private set => SetProperty(ref _failedNotificationCount, value);
    }

    public int? BulkTargetGroupId
    {
        get => _bulkTargetGroupId;
        set => SetProperty(ref _bulkTargetGroupId, value);
    }

    public bool SelectAllVisibleDevices
    {
        get => _selectAllVisibleDevices;
        set
        {
            if (SetProperty(ref _selectAllVisibleDevices, value))
            {
                ToggleAllVisibleDevicesSelection();
            }
        }
    }

    public int AvailabilityPeriodRetentionDays
    {
        get => _availabilityPeriodRetentionDays;
        set => SetProperty(ref _availabilityPeriodRetentionDays, Math.Max(0, value));
    }

    public int IncidentRetentionDays
    {
        get => _incidentRetentionDays;
        set => SetProperty(ref _incidentRetentionDays, Math.Max(0, value));
    }

    public int DailyAggregateRetentionDays
    {
        get => _dailyAggregateRetentionDays;
        set => SetProperty(ref _dailyAggregateRetentionDays, Math.Max(0, value));
    }

    public int HeartbeatGraceSeconds
    {
        get => _heartbeatGraceSeconds;
        set => SetProperty(ref _heartbeatGraceSeconds, Math.Clamp(value, 30, 3600));
    }

    public int ExpectedCheckGraceMultiplier
    {
        get => _expectedCheckGraceMultiplier;
        set => SetProperty(ref _expectedCheckGraceMultiplier, Math.Clamp(value, 1, 10));
    }

    public DowntimeStartPolicy DowntimeStartPolicy
    {
        get => _downtimeStartPolicy;
        set => SetProperty(ref _downtimeStartPolicy, value);
    }

    public string SelectedDeviceCountText => $"{Devices.Count(device => device.IsSelected)} secili";

    public int BulkCheckIntervalSeconds
    {
        get => _bulkCheckIntervalSeconds;
        set => SetProperty(
            ref _bulkCheckIntervalSeconds,
            value <= 0 ? 0 : Math.Clamp(value, AppSettings.MinDeviceCheckIntervalSeconds, AppSettings.MaxDeviceCheckIntervalSeconds));
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

    public string SchedulerStatusText
    {
        get => _schedulerStatusText;
        private set => SetProperty(ref _schedulerStatusText, value ?? "Bilinmiyor");
    }

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

    public string AvailabilityNotice => "Bu ekran gercek cihaz uptime degeri degil, ping tabanli ag erisilebilirligi raporudur. Worker veya beklenen kontrol bosluklari Unknown olarak, planli bakim Maintenance olarak hesaplanir.";

}
