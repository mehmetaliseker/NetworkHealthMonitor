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
    private const string SectionDashboard = "Genel Bakış";
    private const string SectionDevices = "Cihazlar";
    private const string SectionDeviceEdit = "Cihaz Formu";
    private const string SectionGroups = "Cihaz Grupları";
    private const string SectionSchedules = "Kontrol Planları";
    private const string SectionAvailability = "Kesintiler";
    private const string SectionEvents = "Olaylar";
    private const string SectionReports = "Raporlar / Uptime";
    private const string SectionLogs = "Ping Kayıtları";
    private const string SectionNotifications = "Bildirimler";
    private const string SectionSettings = "Ayarlar";
    private const string SettingsGeneral = "Genel";
    private const string SettingsPingRetry = "Ping ve Retry";
    private const string SettingsNtfy = "ntfy";
    private const string SettingsEmailSmtp = "E-posta / SMTP";
    private const string SettingsRecipients = "E-posta Alıcıları";
    private const string SettingsTemplates = "Bildirim Şablonları";
    private const string SettingsRetention = "Veri Saklama";
    private const string SettingsAdvanced = "Gelişmiş";
    private const string SettingsReadiness = "Sistem Durumu";
    private const string AllDeviceTypesText = "Tüm tipler";
    private const string AllStatusesText = "Tüm durumlar";
    private const string AllGroupsText = "Tüm gruplar";
    private const string AllTriggersText = "Tüm kaynaklar";
    private const string AllCriticalText = "Tüm cihazlar";
    private const string CriticalOnlyText = "Yalnızca kritik";
    private const string NonCriticalOnlyText = "Kritik olmayan";
    private const string ActiveDevicesText = "Etkin kayıtlar";
    private const string DeletedDevicesText = "Silinen cihazlar";
    private const string AllDeletionStatesText = "Tüm kayıtlar";
    private const string AllAutoCheckStatesText = "Tüm otomatik kontrol durumları";
    private const string AutoCheckEnabledText = "Otomatik kontrol açık";
    private const string AutoCheckDisabledText = "Otomatik kontrol kapalı";
    private const string AllSuppressionStatesText = "Tüm pasiflik durumları";
    private const string MutedOnlyText = "Bildirimleri susturulan";
    private const string PausedOnlyText = "İzleme duraklatılan";
    private const string NoSuppressionText = "Pasifliği olmayan";
    private const string AllUptimeRangesText = "Tüm uptime değerleri";
    private const string UptimeUnder95Text = "%95 altı";
    private const string UptimeUnder99Text = "%99 altı";
    private const string UptimeUnknownText = "Uptime verisi yok";
    private const string AllLastCheckRangesText = "Tüm son kontrol zamanları";
    private const string LastCheck24HoursText = "Son 24 saat";
    private const string LastCheck7DaysText = "Son 7 gün";
    private const string LastCheckNeverText = "Kontrol edilmedi";
    private const string AllOutboxStatusesText = "Tüm durumlar";
    private const string AllOutboxEventTypesText = "Tüm bildirimler";
    private const string CsvExportAllDevicesText = "Tüm cihazlar";
    private const string CsvExportFilteredDevicesText = "Filtrelenen cihazlar";
    private const string CsvExportSelectedDevicesText = "Seçilen cihazlar";

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
    private readonly IEmailSender _emailSender;
    private readonly INotificationOutboxRepository _notificationOutboxRepository;
    private readonly WorkerHeartbeatRepository _workerHeartbeatRepository;
    private readonly IUiAutostartService _uiAutostartService;

    private CancellationTokenSource? _pingCancellationTokenSource;
    private bool _isInitialized;
    private bool _isBusy;
    private bool _isPinging;
    private bool _isNavigationCollapsed;
    private string _currentSection = SectionDashboard;
    private string _currentSettingsSection = SettingsGeneral;
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
    private string _autoCheckFilter = AllAutoCheckStatesText;
    private string _suppressionFilter = AllSuppressionStatesText;
    private string _uptimeRangeFilter = AllUptimeRangesText;
    private string _lastCheckRangeFilter = AllLastCheckRangesText;
    private bool _deviceOnlyProblematic;
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
    private ScheduleMode _planFormScheduleMode = ScheduleMode.FixedInterval;
    private int _planFormFrequencyValue = AppSettings.DefaultSchedulePlanIntervalMinutes;
    private string _planFormFrequencyUnit = "Dakika";
    private int _planFormTimesPerDay = 4;
    private string _planFormDailyTimes = "08:00;12:00;16:00;20:00";
    private string _planFormSelectedWeekDays = "Monday,Tuesday,Wednesday,Thursday,Friday";
    private bool _planFormFailureRetryEnabled = true;
    private int _planFormConfirmationRetryCount = AppSettings.DefaultFailureRetryLimitValue;
    private int _planFormConfirmationRetryIntervalSeconds = AppSettings.DefaultFailureRetryIntervalSecondsValue;
    private int _planFormOfflineRecheckIntervalValue = 20;
    private string _planFormOfflineRecheckIntervalUnit = "Dakika";
    private MissedRunPolicy _planFormMissedRunPolicy = MissedRunPolicy.SingleCatchUp;
    private string _planFormPreviewText = string.Empty;
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
    private bool _emailEnabled;
    private string _smtpHost = string.Empty;
    private int _smtpPort = 587;
    private SmtpSecurityMode _smtpSecurity = SmtpSecurityMode.StartTls;
    private bool _allowInsecureSmtp;
    private string _smtpUsername = string.Empty;
    private string _smtpPassword = string.Empty;
    private string _senderEmail = string.Empty;
    private string _senderDisplayName = "NetworkHealthMonitor";
    private int _smtpConnectionTimeoutSeconds = 30;
    private int _emailMaxRetryCount = 5;
    private string _testEmailRecipient = string.Empty;
    private int _emailEscalationThresholdHours = NotificationSettings.DefaultEscalationThresholdHours;
    private bool _notificationNotifyOnEscalated = true;
    private bool _emailNotifyOnRecovered;
    private string _newInitialEmailAddress = string.Empty;
    private string _newEscalationEmailAddress = string.Empty;
    private EmailRecipient? _selectedInitialEmailRecipient;
    private EmailRecipient? _selectedEscalationEmailRecipient;
    private string _initialOfflineEmailSubjectTemplate = NotificationTemplateDefaults.InitialOfflineSubject;
    private string _initialOfflineEmailBodyTemplate = NotificationTemplateDefaults.InitialOfflineBody;
    private string _escalationEmailSubjectTemplate = NotificationTemplateDefaults.EscalationSubject;
    private string _escalationEmailBodyTemplate = NotificationTemplateDefaults.EscalationBody;
    private string _recoveredEmailSubjectTemplate = NotificationTemplateDefaults.RecoveredSubject;
    private string _recoveredEmailBodyTemplate = NotificationTemplateDefaults.RecoveredBody;
    private bool _emailTemplatesAreHtml;
    private bool _isSendingTestEmail;
    private string _smtpTestResult = string.Empty;
    private string _notificationLastTestResult = string.Empty;
    private string _notificationLastTestTechnicalDetail = string.Empty;
    private bool _notificationShowTechnicalDetail;
    private bool _isSendingTestNotification;
    private bool _isDeviceFormVisible;
    private bool _isCompactLayout;
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
    private int _bulkSuppressionDurationHours = 24;
    private string _bulkSuppressionReason = string.Empty;
    private bool _selectAllVisibleDevices;
    private CsvImportMode _csvImportMode = CsvImportMode.AddOnly;
    private CsvImportScope _csvImportScope = CsvImportScope.AllActiveDevices;
    private int? _csvImportGroupId;
    private string _csvExportScope = CsvExportFilteredDevicesText;
    private string _csvImportFilePath = string.Empty;
    private string _csvImportPreviewSummary = "CSV önizlemesi henüz alınmadı.";
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
        IUiAutostartService? uiAutostartService = null,
        IEmailSender? emailSender = null)
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
        _emailSender = emailSender ?? new SmtpEmailSender();
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
        AutoCheckFilterOptions = new ObservableCollection<string>(new[] { AllAutoCheckStatesText, AutoCheckEnabledText, AutoCheckDisabledText });
        SuppressionFilterOptions = new ObservableCollection<string>(new[] { AllSuppressionStatesText, MutedOnlyText, PausedOnlyText, NoSuppressionText });
        UptimeRangeFilterOptions = new ObservableCollection<string>(new[] { AllUptimeRangesText, UptimeUnder95Text, UptimeUnder99Text, UptimeUnknownText });
        LastCheckRangeFilterOptions = new ObservableCollection<string>(new[] { AllLastCheckRangesText, LastCheck24HoursText, LastCheck7DaysText, LastCheckNeverText });
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
        ScheduleModeOptions = new ObservableCollection<SelectionOption<ScheduleMode>>(
            Enum.GetValues<ScheduleMode>().Select(mode => new SelectionOption<ScheduleMode>(mode, mode.ToDisplayName())));
        ScheduleIntervalUnitOptions = new ObservableCollection<SelectionOption<ScheduleIntervalUnit>>(
            Enum.GetValues<ScheduleIntervalUnit>().Select(unit => new SelectionOption<ScheduleIntervalUnit>(unit, unit.ToDisplayName())));
        MissedRunPolicyOptions = new ObservableCollection<SelectionOption<MissedRunPolicy>>(
            Enum.GetValues<MissedRunPolicy>().Select(policy => new SelectionOption<MissedRunPolicy>(policy, policy.ToDisplayName())));
        FrequencyUnitOptions = new ObservableCollection<string>(new[] { "Dakika", "Saat", "Gun", "Hafta" });
        ThemeOptions = new ObservableCollection<string>(new[] { "Açık", "Koyu" });
        SmtpSecurityOptions = new ObservableCollection<SelectionOption<SmtpSecurityMode>>(new[]
        {
            new SelectionOption<SmtpSecurityMode>(SmtpSecurityMode.StartTls, "STARTTLS"),
            new SelectionOption<SmtpSecurityMode>(SmtpSecurityMode.SslTls, "SSL/TLS"),
            new SelectionOption<SmtpSecurityMode>(SmtpSecurityMode.None, "Güvenliksiz")
        });
        DeviceTypePolicies = new ObservableCollection<DeviceTypePolicy>(DeviceTypePolicy.CreateDefaults());
        CsvImportModeOptions = new ObservableCollection<SelectionOption<CsvImportMode>>(new[]
        {
            new SelectionOption<CsvImportMode>(CsvImportMode.AddOnly, "Yalnızca yeni cihazları ekle"),
            new SelectionOption<CsvImportMode>(CsvImportMode.Upsert, "Ekle veya güncelle"),
            new SelectionOption<CsvImportMode>(CsvImportMode.Sync, "CSV ile tamamen eşitle")
        });
        CsvImportScopeOptions = new ObservableCollection<SelectionOption<CsvImportScope>>(new[]
        {
            new SelectionOption<CsvImportScope>(CsvImportScope.AllActiveDevices, "Tüm etkin cihazları CSV ile eşitle"),
            new SelectionOption<CsvImportScope>(CsvImportScope.SelectedGroup, "Yalnızca seçili grubu CSV ile eşitle")
        });
        CsvExportScopeOptions = new ObservableCollection<string>(new[]
        {
            CsvExportFilteredDevicesText,
            CsvExportAllDevicesText,
            CsvExportSelectedDevicesText
        });
        OutboxStatusOptions = new ObservableCollection<SelectionOption<string>>(new[]
        {
            new SelectionOption<string>(AllOutboxStatusesText, AllOutboxStatusesText),
            new SelectionOption<string>("Pending", "Bekliyor"),
            new SelectionOption<string>("Processing", "İşleniyor"),
            new SelectionOption<string>("Sent", "Gönderildi"),
            new SelectionOption<string>("Failed", "Başarısız"),
            new SelectionOption<string>("DeadLetter", "Kalıcı hata"),
            new SelectionOption<string>("Cancelled", "İptal edildi")
        });
        OutboxEventTypeOptions = new ObservableCollection<SelectionOption<string>>(new[]
        {
            new SelectionOption<string>(AllOutboxEventTypesText, AllOutboxEventTypesText),
            new SelectionOption<string>(NotificationEventTypes.DeviceSuspectedOffline, "İlk kesinti bildirimi"),
            new SelectionOption<string>(NotificationEventTypes.DeviceOfflineEscalated, "Escalation bildirimi"),
            new SelectionOption<string>("DeviceDown", "Eski kesinti bildirimi"),
            new SelectionOption<string>("DeviceRecovered", "Düzelme bildirimi"),
            new SelectionOption<string>("Test", "Test bildirimi")
        });
        DowntimeStartPolicyOptions = new ObservableCollection<SelectionOption<DowntimeStartPolicy>>(new[]
        {
            new SelectionOption<DowntimeStartPolicy>(DowntimeStartPolicy.FirstFailedCheck, "İlk başarısız kontrol"),
            new SelectionOption<DowntimeStartPolicy>(DowntimeStartPolicy.ConfirmedDownTime, "Kesinti doğrulama zamanı")
        });

        DevicesView = CollectionViewSource.GetDefaultView(Devices);
        DevicesView.Filter = FilterDevice;
        DevicesView.SortDescriptions.Add(new SortDescription(nameof(Device.Name), ListSortDirection.Ascending));
        DevicesView.SortDescriptions.Add(new SortDescription(nameof(Device.IpSortKey), ListSortDirection.Ascending));

        LogsView = CollectionViewSource.GetDefaultView(Logs);
        LogsView.SortDescriptions.Add(new SortDescription(nameof(PingLog.CheckedAt), ListSortDirection.Descending));

        NavigateDashboardCommand = new RelayCommand(() =>
        {
            IsDeviceFormVisible = false;
            CurrentSection = SectionDashboard;
        });
        NavigateDevicesCommand = new RelayCommand(() =>
        {
            IsDeviceFormVisible = false;
            CurrentSection = SectionDevices;
        });
        NavigateDeviceEditCommand = new RelayCommand(() =>
        {
            ClearDeviceForm();
            IsDeviceFormVisible = true;
            CurrentSection = SectionDevices;
        });
        NavigateGroupsCommand = new RelayCommand(() => CurrentSection = SectionGroups);
        NavigateSchedulesCommand = new RelayCommand(() => CurrentSection = SectionSchedules);
        NavigateAvailabilityCommand = new RelayCommand(() => CurrentSection = SectionReports);
        NavigateEventsCommand = new RelayCommand(() => CurrentSection = SectionEvents);
        NavigateReportsCommand = new RelayCommand(() => CurrentSection = SectionReports);
        NavigateMaintenanceCommand = new RelayCommand(() => CurrentSection = SectionMaintenance);
        NavigateCalendarsCommand = new RelayCommand(() => CurrentSection = SectionCalendars);
        NavigateReadinessCommand = new RelayCommand(() => CurrentSection = SectionReadiness);
        NavigateLogsCommand = new RelayCommand(() => CurrentSection = SectionLogs);
        NavigateNotificationsCommand = new RelayCommand(() => CurrentSection = SectionNotifications);
        NavigateSettingsCommand = new RelayCommand(() => CurrentSection = SectionSettings);
        ToggleNavigationCommand = new RelayCommand(() => IsNavigationCollapsed = !IsNavigationCollapsed);
        NavigateSettingsGeneralCommand = new RelayCommand(() => NavigateSettingsSubsection(SettingsGeneral));
        NavigateSettingsPingRetryCommand = new RelayCommand(() => NavigateSettingsSubsection(SettingsPingRetry));
        NavigateSettingsNtfyCommand = new RelayCommand(() => NavigateSettingsSubsection(SettingsNtfy));
        NavigateSettingsEmailSmtpCommand = new RelayCommand(() => NavigateSettingsSubsection(SettingsEmailSmtp));
        NavigateSettingsRecipientsCommand = new RelayCommand(() => NavigateSettingsSubsection(SettingsRecipients));
        NavigateSettingsTemplatesCommand = new RelayCommand(() => NavigateSettingsSubsection(SettingsTemplates));
        NavigateSettingsRetentionCommand = new RelayCommand(() => NavigateSettingsSubsection(SettingsRetention));
        NavigateSettingsAdvancedCommand = new RelayCommand(() => NavigateSettingsSubsection(SettingsAdvanced));
        NavigateSettingsReadinessCommand = new RelayCommand(() => NavigateSettingsSubsection(SettingsReadiness));
        CloseDeviceFormCommand = new RelayCommand(() =>
        {
            ClearDeviceForm();
            IsDeviceFormVisible = false;
        });
        ClearDeviceFiltersCommand = new RelayCommand(ClearDeviceFilters);

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
        MuteNotificationsSelectedCommand = new AsyncRelayCommand<object>(parameter => BulkSetSuppressionAsync(parameter, DeviceSuppressionMode.MuteNotifications), CanUseSelectedDevices);
        PauseMonitoringSelectedCommand = new AsyncRelayCommand<object>(parameter => BulkSetSuppressionAsync(parameter, DeviceSuppressionMode.PauseMonitoring), CanUseSelectedDevices);
        ClearSuppressionSelectedCommand = new AsyncRelayCommand<object>(BulkClearSuppressionAsync, CanUseSelectedDevices);
        SuppressNotificationsCommand = new AsyncRelayCommand<Device>(device => SetDeviceSuppressionAsync(device, DeviceSuppressionMode.MuteNotifications), device => device is { IsDeleted: false } && !IsBusy);
        PauseMonitoringCommand = new AsyncRelayCommand<Device>(device => SetDeviceSuppressionAsync(device, DeviceSuppressionMode.PauseMonitoring), device => device is { IsDeleted: false } && !IsBusy);
        RemoveSuppressionCommand = new AsyncRelayCommand<Device>(ClearDeviceSuppressionAsync, device => device is { IsDeleted: false, IsSuppressionActive: true } && !IsBusy);
        ResumeMonitoringCommand = RemoveSuppressionCommand;
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
        RefreshDevicesCommand = new AsyncRelayCommand(ReloadAllAsync, () => !IsBusy);
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
        AddDeviceCommand = NavigateDeviceEditCommand;
        PingSelectedDevicesCommand = PingSelectedDevicesBulkCommand;
        PingAllDevicesCommand = PingAllCommand;
        ImportCsvCommand = ImportDevicesCommand;
        ExportCsvCommand = ExportDevicesCommand;
        ApplyBulkActionCommand = ApplySelectedCheckIntervalCommand;
        RefreshOutboxCommand = new AsyncRelayCommand(LoadOutboxAsync, () => !IsBusy);
        RetrySelectedOutboxCommand = new AsyncRelayCommand<object>(RetrySelectedOutboxAsync, parameter => !IsBusy && GetSelectedOutboxItems(parameter).Any(item => item.Status is "Failed" or "DeadLetter"));
        RetryOutboxCommand = new AsyncRelayCommand<NotificationOutboxItem>(item => RetryOutboxAsync(item), item => item is { Status: "Failed" or "DeadLetter" } && !IsBusy);
        CancelPendingOutboxCommand = new AsyncRelayCommand<NotificationOutboxItem>(item => CancelPendingOutboxAsync(item), item => item?.Status == "Pending" && !IsBusy);
        ShowOutboxDetailCommand = new RelayCommand<NotificationOutboxItem>(ShowOutboxDetail, item => item is not null);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, () => !IsBusy);
        ResetSettingsCommand = new AsyncRelayCommand(ResetSettingsAsync, () => !IsBusy);
        BackupDatabaseCommand = new AsyncRelayCommand(BackupDatabaseAsync, () => !IsBusy);
        RestoreDatabaseCommand = new AsyncRelayCommand(RestoreDatabaseAsync, () => !IsBusy);
        OptimizeDatabaseCommand = new AsyncRelayCommand(OptimizeDatabaseAsync, () => !IsBusy);
        ExportSettingsCommand = new AsyncRelayCommand(ExportSettingsAsync, () => !IsBusy);
        ImportSettingsCommand = new AsyncRelayCommand(ImportSettingsAsync, () => !IsBusy);
        SendTestNotificationCommand = new AsyncRelayCommand(
            SendTestNotificationAsync,
            () => !IsBusy && !IsSendingTestNotification && CanSendTestNotification);
        SendTestEmailCommand = new AsyncRelayCommand(
            SendTestEmailAsync,
            () => !IsBusy && !IsSendingTestEmail && CanSendTestEmail);
        TestSmtpConnectionCommand = new AsyncRelayCommand(
            TestSmtpConnectionAsync,
            () => !IsBusy && !IsSendingTestEmail && !string.IsNullOrWhiteSpace(SmtpHost));
        AddInitialEmailRecipientCommand = new RelayCommand(AddInitialEmailRecipient, () => !IsBusy);
        RemoveInitialEmailRecipientCommand = new RelayCommand(RemoveInitialEmailRecipient, () => SelectedInitialEmailRecipient is not null && !IsBusy);
        AddEscalationEmailRecipientCommand = new RelayCommand(AddEscalationEmailRecipient, () => !IsBusy);
        RemoveEscalationEmailRecipientCommand = new RelayCommand(RemoveEscalationEmailRecipient, () => SelectedEscalationEmailRecipient is not null && !IsBusy);
        ResetEmailTemplatesCommand = new RelayCommand(ResetEmailTemplates, () => !IsBusy);
        OpenLogFolderCommand = new RelayCommand(OpenLogFolder);

        _schedulerService.StatusChanged += SchedulerStatusChanged;
        EnsureSummaryCards();
        UpdatePlanTargetOptions();
        UpdatePlanPreview();
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

    public ObservableCollection<string> AutoCheckFilterOptions { get; }

    public ObservableCollection<string> SuppressionFilterOptions { get; }

    public ObservableCollection<string> UptimeRangeFilterOptions { get; }

    public ObservableCollection<string> LastCheckRangeFilterOptions { get; }

    public ObservableCollection<string> TriggerFilterOptions { get; }

    public ObservableCollection<SelectionOption<int?>> DeviceGroupOptions { get; } = new();

    public ObservableCollection<SelectionOption<int?>> DeviceOptions { get; } = new();

    public ObservableCollection<SelectionOption<int?>> SchedulePlanOptions { get; } = new();

    public ObservableCollection<SelectionOption<SchedulePlanTargetType>> PlanTargetTypeOptions { get; }

    public ObservableCollection<SelectionOption<string>> PlanTargetOptions { get; } = new();

    public ObservableCollection<SelectionOption<ScheduleMode>> ScheduleModeOptions { get; }

    public ObservableCollection<SelectionOption<ScheduleIntervalUnit>> ScheduleIntervalUnitOptions { get; }

    public ObservableCollection<SelectionOption<MissedRunPolicy>> MissedRunPolicyOptions { get; }

    public ObservableCollection<string> FrequencyUnitOptions { get; }

    public ObservableCollection<string> ThemeOptions { get; }

    public ObservableCollection<SelectionOption<SmtpSecurityMode>> SmtpSecurityOptions { get; }

    public ObservableCollection<DeviceTypePolicy> DeviceTypePolicies { get; }

    public ObservableCollection<EmailRecipient> InitialEmailRecipients { get; } = new();

    public ObservableCollection<EmailRecipient> EscalationEmailRecipients { get; } = new();

    public ObservableCollection<SelectionOption<CsvImportMode>> CsvImportModeOptions { get; }

    public ObservableCollection<SelectionOption<CsvImportScope>> CsvImportScopeOptions { get; }

    public ObservableCollection<string> CsvExportScopeOptions { get; }

    public ObservableCollection<SelectionOption<DowntimeStartPolicy>> DowntimeStartPolicyOptions { get; }

    public ObservableCollection<SelectionOption<string>> OutboxStatusOptions { get; }

    public ObservableCollection<SelectionOption<string>> OutboxEventTypeOptions { get; }

    public ICollectionView DevicesView { get; }

    public ICollectionView LogsView { get; }

    public RelayCommand NavigateDashboardCommand { get; }
    public RelayCommand NavigateDevicesCommand { get; }
    public RelayCommand NavigateDeviceEditCommand { get; }
    public RelayCommand NavigateGroupsCommand { get; }
    public RelayCommand NavigateSchedulesCommand { get; }
    public RelayCommand NavigateAvailabilityCommand { get; }
    public RelayCommand NavigateEventsCommand { get; }
    public RelayCommand NavigateReportsCommand { get; }
    public RelayCommand NavigateLogsCommand { get; }
    public RelayCommand NavigateNotificationsCommand { get; }
    public RelayCommand NavigateSettingsCommand { get; }
    public RelayCommand ToggleNavigationCommand { get; }
    public RelayCommand NavigateSettingsGeneralCommand { get; }
    public RelayCommand NavigateSettingsPingRetryCommand { get; }
    public RelayCommand NavigateSettingsNtfyCommand { get; }
    public RelayCommand NavigateSettingsEmailSmtpCommand { get; }
    public RelayCommand NavigateSettingsRecipientsCommand { get; }
    public RelayCommand NavigateSettingsTemplatesCommand { get; }
    public RelayCommand NavigateSettingsRetentionCommand { get; }
    public RelayCommand NavigateSettingsAdvancedCommand { get; }
    public RelayCommand NavigateSettingsReadinessCommand { get; }
    public RelayCommand CloseDeviceFormCommand { get; }
    public RelayCommand ClearDeviceFiltersCommand { get; }
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
    public AsyncRelayCommand<object> MuteNotificationsSelectedCommand { get; }
    public AsyncRelayCommand<object> PauseMonitoringSelectedCommand { get; }
    public AsyncRelayCommand<object> ClearSuppressionSelectedCommand { get; }
    public AsyncRelayCommand<Device> SuppressNotificationsCommand { get; }
    public AsyncRelayCommand<Device> PauseMonitoringCommand { get; }
    public AsyncRelayCommand<Device> RemoveSuppressionCommand { get; }
    public AsyncRelayCommand<Device> ResumeMonitoringCommand { get; }
    public AsyncRelayCommand<object> DeactivateSelectedDevicesCommand { get; }
    public AsyncRelayCommand<object> DeleteSelectedDevicesBulkCommand { get; }
    public AsyncRelayCommand<object> RestoreSelectedDevicesBulkCommand { get; }
    public RelayCommand AddDeviceCommand { get; }
    public AsyncRelayCommand<object> PingSelectedDevicesCommand { get; }
    public AsyncRelayCommand PingAllDevicesCommand { get; }
    public AsyncRelayCommand ImportCsvCommand { get; }
    public AsyncRelayCommand ExportCsvCommand { get; }
    public AsyncRelayCommand<object> ApplyBulkActionCommand { get; }
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
    public AsyncRelayCommand RefreshDevicesCommand { get; }
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
    public AsyncRelayCommand SendTestEmailCommand { get; }
    public AsyncRelayCommand TestSmtpConnectionCommand { get; }
    public RelayCommand AddInitialEmailRecipientCommand { get; }
    public RelayCommand RemoveInitialEmailRecipientCommand { get; }
    public RelayCommand AddEscalationEmailRecipientCommand { get; }
    public RelayCommand RemoveEscalationEmailRecipientCommand { get; }
    public RelayCommand ResetEmailTemplatesCommand { get; }
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
                OnPropertyChanged(nameof(IsDeviceWorkspaceVisible));
                OnPropertyChanged(nameof(IsGroupsSection));
                OnPropertyChanged(nameof(IsSchedulesSection));
                OnPropertyChanged(nameof(IsAvailabilitySection));
                OnPropertyChanged(nameof(IsEventsSection));
                OnPropertyChanged(nameof(IsReportsSection));
                OnPropertyChanged(nameof(IsMaintenanceSection));
                OnPropertyChanged(nameof(IsCalendarsSection));
                OnPropertyChanged(nameof(IsReadinessSection));
                OnPropertyChanged(nameof(IsLogsSection));
                OnPropertyChanged(nameof(IsNotificationsSection));
                OnPropertyChanged(nameof(IsSettingsSection));
                OnPropertyChanged(nameof(IsDashboardNavSelected));
                OnPropertyChanged(nameof(IsDevicesNavSelected));
                OnPropertyChanged(nameof(IsSchedulesNavSelected));
                OnPropertyChanged(nameof(IsLogsNavSelected));
                OnPropertyChanged(nameof(IsAvailabilityNavSelected));
                OnPropertyChanged(nameof(IsEventsNavSelected));
                OnPropertyChanged(nameof(IsReportsNavSelected));
                OnPropertyChanged(nameof(IsNotificationsNavSelected));
                OnPropertyChanged(nameof(IsSettingsNavSelected));
                OnPropertyChanged(nameof(IsReadinessNavSelected));
                OnPropertyChanged(nameof(IsGroupsNavSelected));
                OnPropertyChanged(nameof(IsMaintenanceNavSelected));
                OnPropertyChanged(nameof(IsCalendarsNavSelected));
            }
        }
    }

    public string CurrentSettingsSection
    {
        get => _currentSettingsSection;
        set
        {
            if (SetProperty(ref _currentSettingsSection, string.IsNullOrWhiteSpace(value) ? SettingsGeneral : value))
            {
                OnPropertyChanged(nameof(IsSettingsGeneralSection));
                OnPropertyChanged(nameof(IsSettingsPingRetrySection));
                OnPropertyChanged(nameof(IsSettingsNtfySection));
                OnPropertyChanged(nameof(IsSettingsEmailSmtpSection));
                OnPropertyChanged(nameof(IsSettingsRecipientsSection));
                OnPropertyChanged(nameof(IsSettingsTemplatesSection));
                OnPropertyChanged(nameof(IsSettingsRetentionSection));
                OnPropertyChanged(nameof(IsSettingsAdvancedSection));
                OnPropertyChanged(nameof(IsSettingsReadinessSection));
            }
        }
    }

    public bool IsNavigationCollapsed
    {
        get => _isNavigationCollapsed;
        set
        {
            if (SetProperty(ref _isNavigationCollapsed, value))
            {
                OnPropertyChanged(nameof(NavigationToggleText));
            }
        }
    }

    public string NavigationToggleText => IsNavigationCollapsed ? "Menüyü genişlet" : "Menüyü daralt";

    public string SectionTitle => CurrentSection switch
    {
        SectionDeviceEdit => SectionDevices,
        _ => CurrentSection
    };

    public string SectionSubtitle => CurrentSection switch
    {
        SectionDashboard => "İzleme servisi durumu, cihaz özeti ve açık kesintileri tek bakışta görün.",
        SectionDevices => "Ağdaki cihazları görüntüleyin, filtreleyin ve yönetin.",
        SectionDeviceEdit => "Cihaz bilgilerini girin veya güncelleyin.",
        SectionGroups => "Kullanıcı tanımlı grupları yönetin ve grup bazlı kontrol çalıştırın.",
        SectionSchedules => "Cihaz, tip, grup veya kritik cihaz bazlı otomatik kontrol planları oluşturun.",
        SectionAvailability => "Erişilebilirlik metriklerini ve kesinti sürelerini inceleyin.",
        SectionEvents => "Açık ve kapanan kesinti olaylarını, bildirim durumlarıyla birlikte izleyin.",
        SectionReports => "Uptime, kapsama ve erişilebilirlik raporlarını tarih aralığına göre inceleyin.",
        SectionMaintenance => "Planlı bakım pencerelerini ve hedeflerini yönetin.",
        SectionCalendars => "İzleme takvimleri, gün/saat kuralları ve cihaz/grup atamalarını yönetin.",
        SectionReadiness => "İzleme servisi, heartbeat, bildirim kuyruğu ve sistem kontrollerini doğrulayın.",
        SectionLogs => "Ping geçmişini filtreleyin, temizleyin ve CSV olarak dışa aktarın.",
        SectionSettings => "Genel, ping, ntfy, e-posta, alıcılar, şablonlar ve veri saklama ayarlarını yönetin.",
        SectionNotifications => "Kanal ve alıcı bazlı bildirim kuyruğunu, hataları ve tekrar denemeleri izleyin.",
        _ => string.Empty
    };

    public bool IsDashboardSection => CurrentSection == SectionDashboard;
    public bool IsDevicesSection => CurrentSection is SectionDevices or SectionDeviceEdit;
    public bool IsDeviceEditSection => CurrentSection == SectionDeviceEdit || (CurrentSection == SectionDevices && IsDeviceFormVisible && IsCompactLayout);
    public bool IsDeviceWorkspaceVisible => CurrentSection is SectionDevices or SectionDeviceEdit;
    public bool IsGroupsSection => CurrentSection == SectionGroups;
    public bool IsSchedulesSection => CurrentSection == SectionSchedules;
    public bool IsAvailabilitySection => CurrentSection == SectionAvailability;
    public bool IsEventsSection => CurrentSection == SectionEvents;
    public bool IsReportsSection => CurrentSection == SectionReports || CurrentSection == SectionAvailability;
    public bool IsLogsSection => CurrentSection == SectionLogs;
    public bool IsNotificationsSection => CurrentSection == SectionNotifications;
    public bool IsSettingsSection => CurrentSection == SectionSettings;
    public bool IsDashboardNavSelected => IsDashboardSection;
    public bool IsDevicesNavSelected => IsDeviceWorkspaceVisible;
    public bool IsSchedulesNavSelected => IsSchedulesSection;
    public bool IsLogsNavSelected => IsLogsSection;
    public bool IsAvailabilityNavSelected => IsReportsSection;
    public bool IsEventsNavSelected => IsEventsSection;
    public bool IsReportsNavSelected => IsReportsSection;
    public bool IsNotificationsNavSelected => IsNotificationsSection;
    public bool IsSettingsNavSelected => IsSettingsSection;
    public bool IsReadinessNavSelected => IsReadinessSection;
    public bool IsGroupsNavSelected => IsGroupsSection;
    public bool IsMaintenanceNavSelected => IsMaintenanceSection;
    public bool IsCalendarsNavSelected => IsCalendarsSection;

    public bool IsSettingsGeneralSection => CurrentSettingsSection == SettingsGeneral;
    public bool IsSettingsPingRetrySection => CurrentSettingsSection == SettingsPingRetry;
    public bool IsSettingsNtfySection => CurrentSettingsSection == SettingsNtfy;
    public bool IsSettingsEmailSmtpSection => CurrentSettingsSection == SettingsEmailSmtp;
    public bool IsSettingsRecipientsSection => CurrentSettingsSection == SettingsRecipients;
    public bool IsSettingsTemplatesSection => CurrentSettingsSection == SettingsTemplates;
    public bool IsSettingsRetentionSection => CurrentSettingsSection == SettingsRetention;
    public bool IsSettingsAdvancedSection => CurrentSettingsSection == SettingsAdvanced;
    public bool IsSettingsReadinessSection => CurrentSettingsSection == SettingsReadiness;

    private void NavigateSettingsSubsection(string subsection)
    {
        CurrentSettingsSection = subsection;
        CurrentSection = SectionSettings;
    }

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
                NotifyDeviceFilterState();
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
                NotifyDeviceFilterState();
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
                NotifyDeviceFilterState();
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
                NotifyDeviceFilterState();
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
                NotifyDeviceFilterState();
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
                NotifyDeviceFilterState();
                RaiseCommandStates();
            }
        }
    }

    public string AutoCheckFilter
    {
        get => _autoCheckFilter;
        set
        {
            if (SetProperty(ref _autoCheckFilter, value ?? AllAutoCheckStatesText))
            {
                DevicesView.Refresh();
                NotifyDeviceFilterState();
            }
        }
    }

    public string SuppressionFilter
    {
        get => _suppressionFilter;
        set
        {
            if (SetProperty(ref _suppressionFilter, value ?? AllSuppressionStatesText))
            {
                DevicesView.Refresh();
                NotifyDeviceFilterState();
            }
        }
    }

    public string UptimeRangeFilter
    {
        get => _uptimeRangeFilter;
        set
        {
            if (SetProperty(ref _uptimeRangeFilter, value ?? AllUptimeRangesText))
            {
                DevicesView.Refresh();
                NotifyDeviceFilterState();
            }
        }
    }

    public string LastCheckRangeFilter
    {
        get => _lastCheckRangeFilter;
        set
        {
            if (SetProperty(ref _lastCheckRangeFilter, value ?? AllLastCheckRangesText))
            {
                DevicesView.Refresh();
                NotifyDeviceFilterState();
            }
        }
    }

    public bool DeviceOnlyProblematic
    {
        get => _deviceOnlyProblematic;
        set
        {
            if (SetProperty(ref _deviceOnlyProblematic, value))
            {
                DevicesView.Refresh();
                NotifyDeviceFilterState();
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

    public ScheduleMode PlanFormScheduleMode
    {
        get => _planFormScheduleMode;
        set
        {
            if (SetProperty(ref _planFormScheduleMode, value))
            {
                UpdatePlanPreview();
            }
        }
    }

    public int PlanFormFrequencyValue
    {
        get => _planFormFrequencyValue;
        set
        {
            if (SetProperty(ref _planFormFrequencyValue, Math.Max(1, value)))
            {
                UpdatePlanPreview();
            }
        }
    }

    public string PlanFormFrequencyUnit
    {
        get => _planFormFrequencyUnit;
        set
        {
            if (SetProperty(ref _planFormFrequencyUnit, value ?? "Dakika"))
            {
                UpdatePlanPreview();
            }
        }
    }

    public int PlanFormTimesPerDay
    {
        get => _planFormTimesPerDay;
        set
        {
            if (SetProperty(ref _planFormTimesPerDay, Math.Clamp(value, 1, 48)))
            {
                UpdatePlanPreview();
            }
        }
    }

    public string PlanFormDailyTimes
    {
        get => _planFormDailyTimes;
        set
        {
            if (SetProperty(ref _planFormDailyTimes, value ?? string.Empty))
            {
                UpdatePlanPreview();
            }
        }
    }

    public string PlanFormSelectedWeekDays
    {
        get => _planFormSelectedWeekDays;
        set
        {
            if (SetProperty(ref _planFormSelectedWeekDays, value ?? string.Empty))
            {
                UpdatePlanPreview();
            }
        }
    }

    public bool PlanFormFailureRetryEnabled
    {
        get => _planFormFailureRetryEnabled;
        set
        {
            if (SetProperty(ref _planFormFailureRetryEnabled, value))
            {
                UpdatePlanPreview();
            }
        }
    }

    public int PlanFormConfirmationRetryCount
    {
        get => _planFormConfirmationRetryCount;
        set
        {
            if (SetProperty(ref _planFormConfirmationRetryCount, Math.Clamp(value, AppSettings.MinConfirmationRetryCount, AppSettings.MaxConfirmationRetryCount)))
            {
                UpdatePlanPreview();
            }
        }
    }

    public int PlanFormConfirmationRetryIntervalSeconds
    {
        get => _planFormConfirmationRetryIntervalSeconds;
        set
        {
            if (SetProperty(ref _planFormConfirmationRetryIntervalSeconds, Math.Clamp(value, AppSettings.MinConfirmationRetryIntervalSeconds, AppSettings.MaxConfirmationRetryIntervalSeconds)))
            {
                UpdatePlanPreview();
            }
        }
    }

    public int PlanFormOfflineRecheckIntervalValue
    {
        get => _planFormOfflineRecheckIntervalValue;
        set
        {
            if (SetProperty(ref _planFormOfflineRecheckIntervalValue, Math.Max(1, value)))
            {
                UpdatePlanPreview();
            }
        }
    }

    public string PlanFormOfflineRecheckIntervalUnit
    {
        get => _planFormOfflineRecheckIntervalUnit;
        set
        {
            if (SetProperty(ref _planFormOfflineRecheckIntervalUnit, value ?? "Dakika"))
            {
                UpdatePlanPreview();
            }
        }
    }

    public MissedRunPolicy PlanFormMissedRunPolicy
    {
        get => _planFormMissedRunPolicy;
        set
        {
            if (SetProperty(ref _planFormMissedRunPolicy, value))
            {
                UpdatePlanPreview();
            }
        }
    }

    public string PlanFormPreviewText
    {
        get => _planFormPreviewText;
        set => SetProperty(ref _planFormPreviewText, value ?? string.Empty);
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
        set
        {
            if (SetProperty(ref _notificationBaseUrl, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CanSendTestNotification));
                RaiseCommandStates();
            }
        }
    }

    public string NotificationTopic
    {
        get => _notificationTopic;
        set
        {
            if (SetProperty(ref _notificationTopic, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CanSendTestNotification));
                RaiseCommandStates();
            }
        }
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

    public bool EmailEnabled
    {
        get => _emailEnabled;
        set => SetProperty(ref _emailEnabled, value);
    }

    public string SmtpHost
    {
        get => _smtpHost;
        set
        {
            if (SetProperty(ref _smtpHost, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CanSendTestEmail));
                RaiseCommandStates();
            }
        }
    }

    public int SmtpPort
    {
        get => _smtpPort;
        set => SetProperty(ref _smtpPort, Math.Clamp(value, 1, 65535));
    }

    public SmtpSecurityMode SmtpSecurity
    {
        get => _smtpSecurity;
        set => SetProperty(ref _smtpSecurity, value);
    }

    public bool AllowInsecureSmtp
    {
        get => _allowInsecureSmtp;
        set => SetProperty(ref _allowInsecureSmtp, value);
    }

    public string SmtpUsername
    {
        get => _smtpUsername;
        set => SetProperty(ref _smtpUsername, value ?? string.Empty);
    }

    public string SmtpPassword
    {
        get => _smtpPassword;
        set => SetProperty(ref _smtpPassword, value ?? string.Empty);
    }

    public string SenderEmail
    {
        get => _senderEmail;
        set
        {
            if (SetProperty(ref _senderEmail, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CanSendTestEmail));
                RaiseCommandStates();
            }
        }
    }

    public string SenderDisplayName
    {
        get => _senderDisplayName;
        set => SetProperty(ref _senderDisplayName, value ?? string.Empty);
    }

    public int SmtpConnectionTimeoutSeconds
    {
        get => _smtpConnectionTimeoutSeconds;
        set => SetProperty(ref _smtpConnectionTimeoutSeconds, Math.Clamp(value, 1, 300));
    }

    public int EmailMaxRetryCount
    {
        get => _emailMaxRetryCount;
        set => SetProperty(ref _emailMaxRetryCount, Math.Clamp(value, 0, 20));
    }

    public string TestEmailRecipient
    {
        get => _testEmailRecipient;
        set
        {
            if (SetProperty(ref _testEmailRecipient, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CanSendTestEmail));
                RaiseCommandStates();
            }
        }
    }

    public int EmailEscalationThresholdHours
    {
        get => _emailEscalationThresholdHours;
        set => SetProperty(
            ref _emailEscalationThresholdHours,
            Math.Clamp(value, NotificationSettings.MinEscalationThresholdHours, NotificationSettings.MaxEscalationThresholdHours));
    }

    public bool NotificationNotifyOnEscalated
    {
        get => _notificationNotifyOnEscalated;
        set => SetProperty(ref _notificationNotifyOnEscalated, value);
    }

    public bool EmailNotifyOnRecovered
    {
        get => _emailNotifyOnRecovered;
        set => SetProperty(ref _emailNotifyOnRecovered, value);
    }

    public string NewInitialEmailAddress
    {
        get => _newInitialEmailAddress;
        set => SetProperty(ref _newInitialEmailAddress, value ?? string.Empty);
    }

    public string NewEscalationEmailAddress
    {
        get => _newEscalationEmailAddress;
        set => SetProperty(ref _newEscalationEmailAddress, value ?? string.Empty);
    }

    public EmailRecipient? SelectedInitialEmailRecipient
    {
        get => _selectedInitialEmailRecipient;
        set
        {
            if (SetProperty(ref _selectedInitialEmailRecipient, value))
            {
                RemoveInitialEmailRecipientCommand?.NotifyCanExecuteChanged();
            }
        }
    }

    public EmailRecipient? SelectedEscalationEmailRecipient
    {
        get => _selectedEscalationEmailRecipient;
        set
        {
            if (SetProperty(ref _selectedEscalationEmailRecipient, value))
            {
                RemoveEscalationEmailRecipientCommand?.NotifyCanExecuteChanged();
            }
        }
    }

    public string InitialOfflineEmailSubjectTemplate
    {
        get => _initialOfflineEmailSubjectTemplate;
        set
        {
            if (SetProperty(ref _initialOfflineEmailSubjectTemplate, value ?? string.Empty))
            {
                RaiseEmailTemplatePreviewProperties();
            }
        }
    }

    public string InitialOfflineEmailBodyTemplate
    {
        get => _initialOfflineEmailBodyTemplate;
        set
        {
            if (SetProperty(ref _initialOfflineEmailBodyTemplate, value ?? string.Empty))
            {
                RaiseEmailTemplatePreviewProperties();
            }
        }
    }

    public string EscalationEmailSubjectTemplate
    {
        get => _escalationEmailSubjectTemplate;
        set
        {
            if (SetProperty(ref _escalationEmailSubjectTemplate, value ?? string.Empty))
            {
                RaiseEmailTemplatePreviewProperties();
            }
        }
    }

    public string EscalationEmailBodyTemplate
    {
        get => _escalationEmailBodyTemplate;
        set
        {
            if (SetProperty(ref _escalationEmailBodyTemplate, value ?? string.Empty))
            {
                RaiseEmailTemplatePreviewProperties();
            }
        }
    }

    public string RecoveredEmailSubjectTemplate
    {
        get => _recoveredEmailSubjectTemplate;
        set
        {
            if (SetProperty(ref _recoveredEmailSubjectTemplate, value ?? string.Empty))
            {
                RaiseEmailTemplatePreviewProperties();
            }
        }
    }

    public string RecoveredEmailBodyTemplate
    {
        get => _recoveredEmailBodyTemplate;
        set
        {
            if (SetProperty(ref _recoveredEmailBodyTemplate, value ?? string.Empty))
            {
                RaiseEmailTemplatePreviewProperties();
            }
        }
    }

    public bool EmailTemplatesAreHtml
    {
        get => _emailTemplatesAreHtml;
        set => SetProperty(ref _emailTemplatesAreHtml, value);
    }

    public bool IsSendingTestEmail
    {
        get => _isSendingTestEmail;
        private set
        {
            if (SetProperty(ref _isSendingTestEmail, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string SmtpTestResult
    {
        get => _smtpTestResult;
        private set => SetProperty(ref _smtpTestResult, value ?? string.Empty);
    }

    public string AvailableEmailTemplatePlaceholdersText =>
        string.Join(", ", NotificationTemplatePlaceholders.All.Select(item => "{" + item + "}"));

    public string InitialEmailPreviewText => RenderPreview(InitialOfflineEmailSubjectTemplate, InitialOfflineEmailBodyTemplate);

    public string EscalationEmailPreviewText => RenderPreview(EscalationEmailSubjectTemplate, EscalationEmailBodyTemplate);

    public string EmailTemplateValidationText => BuildTemplateValidationText();

    public string NotificationLastTestResult
    {
        get => _notificationLastTestResult;
        private set => SetProperty(ref _notificationLastTestResult, value ?? string.Empty);
    }

    public string NotificationLastTestTechnicalDetail
    {
        get => _notificationLastTestTechnicalDetail;
        private set => SetProperty(ref _notificationLastTestTechnicalDetail, value ?? string.Empty);
    }

    public bool NotificationShowTechnicalDetail
    {
        get => _notificationShowTechnicalDetail;
        set => SetProperty(ref _notificationShowTechnicalDetail, value);
    }

    public bool IsSendingTestNotification
    {
        get => _isSendingTestNotification;
        private set
        {
            if (SetProperty(ref _isSendingTestNotification, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool CanSendTestNotification =>
        !string.IsNullOrWhiteSpace(NotificationBaseUrl)
        && !string.IsNullOrWhiteSpace(NotificationTopic)
        && NtfyPublishRequestFactory.IsTopicValid(NotificationTopic);

    public bool CanSendTestEmail =>
        !string.IsNullOrWhiteSpace(SmtpHost)
        && EmailAddressValidator.IsValid(SenderEmail)
        && EmailAddressValidator.IsValid(TestEmailRecipient);

    private void RaiseEmailTemplatePreviewProperties()
    {
        OnPropertyChanged(nameof(InitialEmailPreviewText));
        OnPropertyChanged(nameof(EscalationEmailPreviewText));
        OnPropertyChanged(nameof(EmailTemplateValidationText));
    }

    private string RenderPreview(string subjectTemplate, string bodyTemplate)
    {
        var renderer = new NotificationTemplateRenderer();
        try
        {
            var subject = renderer.Render(subjectTemplate, CreateSampleTemplateContext()).Trim();
            var body = renderer.Render(bodyTemplate, CreateSampleTemplateContext()).Trim();
            return $"Konu: {subject}{Environment.NewLine}{Environment.NewLine}{body}";
        }
        catch (Exception ex)
        {
            return "Onizleme olusturulamadi: " + ex.Message;
        }
    }

    private string BuildTemplateValidationText()
    {
        var renderer = new NotificationTemplateRenderer();
        var unknown = new[]
            {
                InitialOfflineEmailSubjectTemplate,
                InitialOfflineEmailBodyTemplate,
                EscalationEmailSubjectTemplate,
                EscalationEmailBodyTemplate,
                RecoveredEmailSubjectTemplate,
                RecoveredEmailBodyTemplate
            }
            .SelectMany(renderer.FindUnknownPlaceholders)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (unknown.Count > 0)
        {
            return "Bilinmeyen degisken: " + string.Join(", ", unknown.Select(item => "{" + item + "}"));
        }

        if (string.IsNullOrWhiteSpace(InitialOfflineEmailSubjectTemplate)
            || string.IsNullOrWhiteSpace(EscalationEmailSubjectTemplate)
            || string.IsNullOrWhiteSpace(RecoveredEmailSubjectTemplate))
        {
            return "E-posta konu sablonlari bos birakilamaz.";
        }

        return "Sablonlar gecerli.";
    }

    private static NotificationTemplateContext CreateSampleTemplateContext()
    {
        return new NotificationTemplateContext
        {
            DeviceName = "Ornek Kamera",
            IpAddress = "192.0.2.10",
            DeviceType = DeviceType.Camera.ToDisplayName(),
            GroupName = "Merkez",
            Status = DeviceStatus.Offline.ToDisplayName(),
            IncidentStartedAtUtc = DateTime.UtcNow.AddHours(-2),
            LastSuccessfulCheckAtUtc = DateTime.UtcNow.AddHours(-3),
            LastCheckAtUtc = DateTime.UtcNow,
            OfflineDuration = TimeSpan.FromHours(2),
            EscalationThreshold = TimeSpan.FromHours(NotificationSettings.DefaultEscalationThresholdHours)
        };
    }

    public bool IsDeviceFormVisible
    {
        get => _isDeviceFormVisible;
        set
        {
            if (SetProperty(ref _isDeviceFormVisible, value))
            {
                OnPropertyChanged(nameof(IsDeviceEditSection));
                OnPropertyChanged(nameof(ShowDeviceListPane));
                OnPropertyChanged(nameof(ShowDeviceFormPane));
            }
        }
    }

    public bool IsCompactLayout
    {
        get => _isCompactLayout;
        set
        {
            if (SetProperty(ref _isCompactLayout, value))
            {
                OnPropertyChanged(nameof(IsDeviceEditSection));
                OnPropertyChanged(nameof(ShowDeviceListPane));
                OnPropertyChanged(nameof(ShowDeviceFormPane));
                OnPropertyChanged(nameof(DeviceWorkspaceColumns));
            }
        }
    }

    public bool ShowDeviceListPane => IsDeviceWorkspaceVisible && (!IsCompactLayout || !IsDeviceFormVisible);

    public bool ShowDeviceFormPane => IsDeviceWorkspaceVisible && IsDeviceFormVisible;

    public string DeviceWorkspaceColumns => IsCompactLayout || !IsDeviceFormVisible ? "*" : "*,560";

    private void NotifyDeviceFilterState()
    {
        OnPropertyChanged(nameof(ActiveDeviceFilterCount));
        OnPropertyChanged(nameof(ActiveDeviceFilterCountText));
        OnPropertyChanged(nameof(ActiveDeviceFilterSummaryText));
        OnPropertyChanged(nameof(HasNoFilteredDevices));
    }

    public int ActiveDeviceFilterCount
    {
        get
        {
            var count = 0;
            if (!string.IsNullOrWhiteSpace(DeviceSearchText))
            {
                count++;
            }

            if (DeviceTypeFilter != AllDeviceTypesText)
            {
                count++;
            }

            if (DeviceStatusFilter != AllStatusesText)
            {
                count++;
            }

            if (DeviceGroupFilter != AllGroupsText)
            {
                count++;
            }

            if (CriticalFilter != AllCriticalText)
            {
                count++;
            }

            if (DeletedDeviceFilter != ActiveDevicesText)
            {
                count++;
            }

            if (AutoCheckFilter != AllAutoCheckStatesText)
            {
                count++;
            }

            if (SuppressionFilter != AllSuppressionStatesText)
            {
                count++;
            }

            if (UptimeRangeFilter != AllUptimeRangesText)
            {
                count++;
            }

            if (LastCheckRangeFilter != AllLastCheckRangesText)
            {
                count++;
            }

            if (DeviceOnlyProblematic)
            {
                count++;
            }

            return count;
        }
    }

    public string ActiveDeviceFilterCountText =>
        ActiveDeviceFilterCount == 0
            ? "Filtre yok"
            : $"{ActiveDeviceFilterCount} aktif filtre";

    public string ActiveDeviceFilterSummaryText
    {
        get
        {
            var filters = new List<string>();
            if (!string.IsNullOrWhiteSpace(DeviceSearchText))
            {
                filters.Add($"Arama: {DeviceSearchText}");
            }

            AddIfActive(filters, DeviceTypeFilter, AllDeviceTypesText);
            AddIfActive(filters, DeviceStatusFilter, AllStatusesText);
            AddIfActive(filters, DeviceGroupFilter, AllGroupsText);
            AddIfActive(filters, CriticalFilter, AllCriticalText);
            AddIfActive(filters, DeletedDeviceFilter, ActiveDevicesText);
            AddIfActive(filters, AutoCheckFilter, AllAutoCheckStatesText);
            AddIfActive(filters, SuppressionFilter, AllSuppressionStatesText);
            AddIfActive(filters, UptimeRangeFilter, AllUptimeRangesText);
            AddIfActive(filters, LastCheckRangeFilter, AllLastCheckRangesText);

            if (DeviceOnlyProblematic)
            {
                filters.Add("Yalnızca sorunlu cihazlar");
            }

            return filters.Count == 0 ? "Filtre yok" : string.Join(" · ", filters);
        }
    }

    private static void AddIfActive(List<string> filters, string value, string defaultValue)
    {
        if (!string.Equals(value, defaultValue, StringComparison.Ordinal))
        {
            filters.Add(value);
        }
    }

    public bool HasNoFilteredDevices => DevicesView is { IsEmpty: true };

    public int ActiveDeviceCount => Devices.Count(device => !device.IsDeleted && device.IsEnabled && device.IsActive);

    public int UnavailableDeviceCount => Devices.Count(device => !device.IsDeleted && device.LastStatus == DeviceStatus.Offline);

    public int MutedDeviceCount => Devices.Count(device =>
        !device.IsDeleted
        && device.IsSuppressionActive
        && device.SuppressionMode == DeviceSuppressionMode.MuteNotifications);

    public int PausedDeviceCount => Devices.Count(device => !device.IsDeleted && device.IsMonitoringPaused);

    public int UnknownDeviceCount => Devices.Count(device => !device.IsDeleted && device.LastStatus == DeviceStatus.Unknown);

    public int PendingOrFailedNotificationCount => PendingNotificationCount + FailedNotificationCount;

    public bool HasSelectedDevices => Devices.Any(device => device.IsSelected);

    public bool IsWorkerServiceStopped =>
        WorkerHealthText.Contains("çalışmıyor", StringComparison.OrdinalIgnoreCase)
        || WorkerHealthText.Contains("durdu", StringComparison.OrdinalIgnoreCase)
        || WorkerHealthText.Contains("bulunamadı", StringComparison.OrdinalIgnoreCase)
        || WorkerHealthText.Contains("yanıt vermiyor", StringComparison.OrdinalIgnoreCase);

    private void NotifyShellMetrics()
    {
        OnPropertyChanged(nameof(ActiveDeviceCount));
        OnPropertyChanged(nameof(UnavailableDeviceCount));
        OnPropertyChanged(nameof(MutedDeviceCount));
        OnPropertyChanged(nameof(PausedDeviceCount));
        OnPropertyChanged(nameof(UnknownDeviceCount));
        OnPropertyChanged(nameof(HasSelectedDevices));
        OnPropertyChanged(nameof(SelectedDeviceCountText));
        OnPropertyChanged(nameof(PingSelectedDevicesText));
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
        private set
        {
            if (SetProperty(ref _workerHealthText, value ?? "Bilinmiyor"))
            {
                OnPropertyChanged(nameof(IsWorkerServiceStopped));
            }
        }
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
        private set
        {
            if (SetProperty(ref _pendingNotificationCount, value))
            {
                OnPropertyChanged(nameof(PendingOrFailedNotificationCount));
            }
        }
    }

    public int FailedNotificationCount
    {
        get => _failedNotificationCount;
        private set
        {
            if (SetProperty(ref _failedNotificationCount, value))
            {
                OnPropertyChanged(nameof(PendingOrFailedNotificationCount));
            }
        }
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

    public string SelectedDeviceCountText => $"{Devices.Count(device => device.IsSelected)} cihaz seçildi";

    public string PingSelectedDevicesText => Devices.Count(device => device.IsSelected) > 0
        ? $"Seçilenlere Ping At ({Devices.Count(device => device.IsSelected)})"
        : "Seçilenlere Ping At";

    public string CsvExportScope
    {
        get => _csvExportScope;
        set
        {
            if (SetProperty(ref _csvExportScope, string.IsNullOrWhiteSpace(value) ? CsvExportFilteredDevicesText : value))
            {
                ExportDevicesCommand?.NotifyCanExecuteChanged();
            }
        }
    }

    public int BulkCheckIntervalSeconds
    {
        get => _bulkCheckIntervalSeconds;
        set => SetProperty(
            ref _bulkCheckIntervalSeconds,
            value <= 0 ? 0 : Math.Clamp(value, AppSettings.MinDeviceCheckIntervalSeconds, AppSettings.MaxDeviceCheckIntervalSeconds));
    }

    public int BulkSuppressionDurationHours
    {
        get => _bulkSuppressionDurationHours;
        set => SetProperty(ref _bulkSuppressionDurationHours, Math.Clamp(value, 0, NotificationSettings.MaxEscalationThresholdHours));
    }

    public string BulkSuppressionReason
    {
        get => _bulkSuppressionReason;
        set => SetProperty(ref _bulkSuppressionReason, value ?? string.Empty);
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

    public string AvailabilityNotice => "Bu ekran gerçek cihaz uptime değeri değil; ping tabanlı ağ erişilebilirliği raporudur. İzleme servisi veya beklenen kontrol boşlukları kontrol edilmedi olarak, planlı bakım bakım durumunda hesaplanır.";

}
