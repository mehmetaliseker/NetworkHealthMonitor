using System.Collections.ObjectModel;
using System.Globalization;
using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Infrastructure;
using NetworkHealthMonitor.Models;
using NetworkHealthMonitor.Services;

namespace NetworkHealthMonitor.ViewModels;

public sealed partial class MainViewModel
{
    private const string SectionMaintenance = "Bakım";
    private const string SectionCalendars = "İzleme Takvimleri";
    private const string SectionReadiness = "Sistem Durumu";

    private readonly MaintenanceWindowRepository _maintenanceWindowRepository;
    private readonly MonitoringCalendarRepository _monitoringCalendarRepository;
    private readonly SystemReadinessService _systemReadinessService;

    private MaintenanceWindowListItem? _selectedMaintenanceWindow;
    private MonitoringCalendarListItem? _selectedMonitoringCalendar;
    private DeviceAvailabilityDetail _selectedDeviceAvailabilityDetail = new();
    private string _selectedTimelineRange = "Son 30 gün";
    private DateTime? _customTimelineStartDate;
    private DateTime? _customTimelineEndDate;
    private long? _editingMaintenanceWindowId;
    private string _maintenanceFormName = string.Empty;
    private DateTime? _maintenanceFormStart = DateTime.Now;
    private DateTime? _maintenanceFormEnd = DateTime.Now.AddHours(1);
    private string _maintenanceFormReason = string.Empty;
    private MonitoringTargetType _maintenanceFormTargetType = MonitoringTargetType.AllDevices;
    private int? _maintenanceFormTargetId;
    private bool _maintenanceSuppressNotifications = true;
    private bool _maintenanceContinuePings = true;
    private long? _editingCalendarId;
    private string _calendarFormName = string.Empty;
    private string _calendarTimezoneId = TimeZoneInfo.Local.Id;
    private bool _calendarIsDefault;
    private bool _calendarIs24x7 = true;
    private bool _calendarMonday = true;
    private bool _calendarTuesday = true;
    private bool _calendarWednesday = true;
    private bool _calendarThursday = true;
    private bool _calendarFriday = true;
    private bool _calendarSaturday = true;
    private bool _calendarSunday = true;
    private string _calendarStartTime = "08:00";
    private string _calendarEndTime = "18:00";
    private MonitoringTargetType _calendarAssignmentTargetType = MonitoringTargetType.AllDevices;
    private int? _calendarAssignmentTargetId;
    private double? _formSlaTargetAvailabilityPercent;
    private double? _groupFormTargetAvailabilityPercent;
    private double? _dashboardAvailability24Hours;
    private double? _dashboardAvailability7Days;
    private double? _dashboardAvailability30Days;
    private double? _dashboardCoverage30Days;
    private int _dashboardSlaViolationCount;

    public RelayCommand NavigateMaintenanceCommand { get; }
    public RelayCommand NavigateCalendarsCommand { get; }
    public RelayCommand NavigateReadinessCommand { get; }
    public AsyncRelayCommand SaveMaintenanceWindowCommand { get; }
    public RelayCommand ClearMaintenanceWindowFormCommand { get; }
    public RelayCommand EditSelectedMaintenanceWindowCommand { get; }
    public AsyncRelayCommand CancelSelectedMaintenanceWindowCommand { get; }
    public AsyncRelayCommand CompleteSelectedMaintenanceWindowCommand { get; }
    public AsyncRelayCommand SaveMonitoringCalendarCommand { get; }
    public RelayCommand ClearMonitoringCalendarFormCommand { get; }
    public RelayCommand EditSelectedMonitoringCalendarCommand { get; }
    public AsyncRelayCommand DeleteSelectedMonitoringCalendarCommand { get; }
    public AsyncRelayCommand RefreshReadinessCommand { get; }
    public AsyncRelayCommand RefreshSelectedDeviceTimelineCommand { get; }

    public ObservableCollection<AvailabilityTrendPoint> DashboardAvailabilityTrend { get; } = new();
    public ObservableCollection<AvailabilityRankingRow> LongestOutages { get; } = new();
    public ObservableCollection<AvailabilityRankingRow> IncidentRanking { get; } = new();
    public ObservableCollection<AvailabilityRankingRow> UnknownDurationRanking { get; } = new();
    public ObservableCollection<AvailabilityRankingRow> LowCoverageRanking { get; } = new();
    public ObservableCollection<MaintenanceWindowListItem> MaintenanceWindows { get; } = new();
    public ObservableCollection<MonitoringCalendarListItem> MonitoringCalendars { get; } = new();
    public ObservableCollection<ReadinessCheckItem> ServiceReadinessChecks { get; } = new();
    public ObservableCollection<ReadinessCheckItem> ServiceDiagnostics { get; } = new();
    public ObservableCollection<DeviceAvailabilityPeriod> SelectedDeviceAvailabilityTimeline { get; } = new();
    public ObservableCollection<string> TimelineRangeOptions { get; } = new(new[] { "Son 24 saat", "Son 7 gün", "Son 30 gün", "Bu ay", "Özel tarih aralığı" });
    public ObservableCollection<SelectionOption<MonitoringTargetType>> MonitoringTargetTypeOptions { get; } = new(new[]
    {
        new SelectionOption<MonitoringTargetType>(MonitoringTargetType.AllDevices, "Tüm cihazlar"),
        new SelectionOption<MonitoringTargetType>(MonitoringTargetType.Group, "Grup"),
        new SelectionOption<MonitoringTargetType>(MonitoringTargetType.Device, "Cihaz")
    });
    public ObservableCollection<SelectionOption<double?>> SlaTargetOptions { get; } = new(new[]
    {
        new SelectionOption<double?>((double?)null, "Tanımlı değil"),
        new SelectionOption<double?>(99d, "%99"),
        new SelectionOption<double?>(99.5d, "%99.5"),
        new SelectionOption<double?>(99.9d, "%99.9"),
        new SelectionOption<double?>(99.95d, "%99.95"),
        new SelectionOption<double?>(99.99d, "%99.99")
    });

    public bool IsMaintenanceSection => CurrentSection == SectionMaintenance;
    public bool IsCalendarsSection => CurrentSection == SectionCalendars;
    public bool IsReadinessSection => CurrentSection == SectionReadiness;

    public MaintenanceWindowListItem? SelectedMaintenanceWindow
    {
        get => _selectedMaintenanceWindow;
        set
        {
            if (SetProperty(ref _selectedMaintenanceWindow, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public MonitoringCalendarListItem? SelectedMonitoringCalendar
    {
        get => _selectedMonitoringCalendar;
        set
        {
            if (SetProperty(ref _selectedMonitoringCalendar, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public DeviceAvailabilityDetail SelectedDeviceAvailabilityDetail
    {
        get => _selectedDeviceAvailabilityDetail;
        set => SetProperty(ref _selectedDeviceAvailabilityDetail, value ?? new DeviceAvailabilityDetail());
    }

    public string SelectedTimelineRange
    {
        get => _selectedTimelineRange;
        set
        {
            if (SetProperty(ref _selectedTimelineRange, value ?? "Son 30 gün"))
            {
                _ = RefreshSelectedDeviceAvailabilityAsync();
            }
        }
    }

    public DateTime? CustomTimelineStartDate
    {
        get => _customTimelineStartDate;
        set => SetProperty(ref _customTimelineStartDate, value);
    }

    public DateTime? CustomTimelineEndDate
    {
        get => _customTimelineEndDate;
        set => SetProperty(ref _customTimelineEndDate, value);
    }

    public string MaintenanceFormName
    {
        get => _maintenanceFormName;
        set => SetProperty(ref _maintenanceFormName, value ?? string.Empty);
    }

    public DateTime? MaintenanceFormStart
    {
        get => _maintenanceFormStart;
        set => SetProperty(ref _maintenanceFormStart, value);
    }

    public DateTime? MaintenanceFormEnd
    {
        get => _maintenanceFormEnd;
        set => SetProperty(ref _maintenanceFormEnd, value);
    }

    public string MaintenanceFormReason
    {
        get => _maintenanceFormReason;
        set => SetProperty(ref _maintenanceFormReason, value ?? string.Empty);
    }

    public MonitoringTargetType MaintenanceFormTargetType
    {
        get => _maintenanceFormTargetType;
        set => SetProperty(ref _maintenanceFormTargetType, value);
    }

    public int? MaintenanceFormTargetId
    {
        get => _maintenanceFormTargetId;
        set => SetProperty(ref _maintenanceFormTargetId, value);
    }

    public bool MaintenanceSuppressNotifications
    {
        get => _maintenanceSuppressNotifications;
        set => SetProperty(ref _maintenanceSuppressNotifications, value);
    }

    public bool MaintenanceContinuePings
    {
        get => _maintenanceContinuePings;
        set => SetProperty(ref _maintenanceContinuePings, value);
    }

    public string MaintenanceFormActionText => _editingMaintenanceWindowId.HasValue ? "Güncelle" : "Bakım oluştur";

    public string CalendarFormName
    {
        get => _calendarFormName;
        set => SetProperty(ref _calendarFormName, value ?? string.Empty);
    }

    public string CalendarTimezoneId
    {
        get => _calendarTimezoneId;
        set => SetProperty(ref _calendarTimezoneId, string.IsNullOrWhiteSpace(value) ? TimeZoneInfo.Local.Id : value);
    }

    public bool CalendarIsDefault
    {
        get => _calendarIsDefault;
        set => SetProperty(ref _calendarIsDefault, value);
    }

    public bool CalendarIs24x7
    {
        get => _calendarIs24x7;
        set => SetProperty(ref _calendarIs24x7, value);
    }

    public bool CalendarMonday { get => _calendarMonday; set => SetProperty(ref _calendarMonday, value); }
    public bool CalendarTuesday { get => _calendarTuesday; set => SetProperty(ref _calendarTuesday, value); }
    public bool CalendarWednesday { get => _calendarWednesday; set => SetProperty(ref _calendarWednesday, value); }
    public bool CalendarThursday { get => _calendarThursday; set => SetProperty(ref _calendarThursday, value); }
    public bool CalendarFriday { get => _calendarFriday; set => SetProperty(ref _calendarFriday, value); }
    public bool CalendarSaturday { get => _calendarSaturday; set => SetProperty(ref _calendarSaturday, value); }
    public bool CalendarSunday { get => _calendarSunday; set => SetProperty(ref _calendarSunday, value); }

    public string CalendarStartTime
    {
        get => _calendarStartTime;
        set => SetProperty(ref _calendarStartTime, value ?? string.Empty);
    }

    public string CalendarEndTime
    {
        get => _calendarEndTime;
        set => SetProperty(ref _calendarEndTime, value ?? string.Empty);
    }

    public MonitoringTargetType CalendarAssignmentTargetType
    {
        get => _calendarAssignmentTargetType;
        set => SetProperty(ref _calendarAssignmentTargetType, value);
    }

    public int? CalendarAssignmentTargetId
    {
        get => _calendarAssignmentTargetId;
        set => SetProperty(ref _calendarAssignmentTargetId, value);
    }

    public string CalendarFormActionText => _editingCalendarId.HasValue ? "Güncelle" : "Takvim oluştur";

    public double? FormSlaTargetAvailabilityPercent
    {
        get => _formSlaTargetAvailabilityPercent;
        set => SetProperty(ref _formSlaTargetAvailabilityPercent, value.HasValue ? Math.Clamp(value.Value, 0d, 100d) : null);
    }

    public double? GroupFormTargetAvailabilityPercent
    {
        get => _groupFormTargetAvailabilityPercent;
        set => SetProperty(ref _groupFormTargetAvailabilityPercent, value.HasValue ? Math.Clamp(value.Value, 0d, 100d) : null);
    }

    private async Task LoadDashboardAnalyticsAsync()
    {
        var now = DateTime.UtcNow;
        var start = now.AddDays(-30);
        var summary24 = await _availabilityService.GetAvailabilitySummaryAsync(now.AddHours(-24), now, TimeZoneInfo.Local.Id);
        var summary7 = await _availabilityService.GetAvailabilitySummaryAsync(now.AddDays(-7), now, TimeZoneInfo.Local.Id);
        var summary30 = await _availabilityService.GetAvailabilitySummaryAsync(start, now, TimeZoneInfo.Local.Id);
        _dashboardAvailability24Hours = CalculateWeightedAvailability(summary24);
        _dashboardAvailability7Days = CalculateWeightedAvailability(summary7);
        _dashboardAvailability30Days = CalculateWeightedAvailability(summary30);
        _dashboardCoverage30Days = CalculateWeightedCoverage(summary30);
        _dashboardSlaViolationCount = summary30.Count(item => string.Equals(item.SlaStatus, "Ihlal", StringComparison.OrdinalIgnoreCase));

        var trend = await _availabilityService.GetDailyTrendAsync(start, now, TimeZoneInfo.Local.Id);
        ReplaceCollection(DashboardAvailabilityTrend, trend);
        ReplaceCollection(LongestOutages, await _availabilityService.GetLongestOutagesAsync(start, now, 10));
        ReplaceCollection(IncidentRanking, await _availabilityService.GetIncidentRankingAsync(start, now, 10));
        ReplaceCollection(UnknownDurationRanking, AvailabilityItems
            .OrderByDescending(item => item.UnknownSeconds)
            .Take(10)
            .Select(item => new AvailabilityRankingRow
            {
                Title = item.DeviceName,
                Subtitle = item.IpAddress,
                Value = AvailabilitySummaryReportItem.FormatDuration(item.UnknownSeconds),
                Percent = Math.Min(100, item.UnknownSeconds / 864d)
            }));
        ReplaceCollection(LowCoverageRanking, AvailabilityItems
            .Where(item => item.CoveragePercent.HasValue)
            .OrderBy(item => item.CoveragePercent)
            .Take(10)
            .Select(item => new AvailabilityRankingRow
            {
                Title = item.DeviceName,
                Subtitle = item.IpAddress,
                Value = item.CoverageText,
                Percent = item.CoveragePercent ?? 0
            }));
    }

    private async Task TryLoadDashboardAnalyticsAsync()
    {
        try
        {
            await LoadDashboardAnalyticsAsync();
        }
        catch (Exception ex)
        {
            AppErrorLogger.Log(ex, "DashboardAnalytics");
            ReplaceCollection(DashboardAvailabilityTrend, Array.Empty<AvailabilityTrendPoint>());
            ReplaceCollection(LongestOutages, Array.Empty<AvailabilityRankingRow>());
            ReplaceCollection(IncidentRanking, new[]
            {
                new AvailabilityRankingRow
                {
                    Title = "Analitik yüklenemedi",
                    Subtitle = "Detaylar log dosyasında.",
                    Value = "Hata",
                    Percent = 0
                }
            });
            StatusMessage = "Dashboard analitiği yüklenemedi; cihaz, plan, manuel ping ve ayarlar kullanılabilir.";
        }
    }

    private static double? CalculateWeightedAvailability(IEnumerable<AvailabilitySummaryReportItem> items)
    {
        var materialized = items.ToList();
        var known = materialized.Sum(item => item.UpSeconds + item.DownSeconds);
        return known > 0 ? materialized.Sum(item => item.UpSeconds) * 100d / known : null;
    }

    private static double? CalculateWeightedCoverage(IEnumerable<AvailabilitySummaryReportItem> items)
    {
        var materialized = items.ToList();
        var expected = materialized.Sum(item => item.ExpectedMonitoringSeconds);
        var known = materialized.Sum(item => item.UpSeconds + item.DownSeconds);
        return expected > 0 ? known * 100d / expected : null;
    }

    private async Task LoadMaintenanceWindowsAsync()
    {
        ReplaceCollection(MaintenanceWindows, await _maintenanceWindowRepository.GetAllAsync());
    }

    private async Task LoadMonitoringCalendarsAsync()
    {
        ReplaceCollection(MonitoringCalendars, await _monitoringCalendarRepository.GetAllAsync());
    }

    private async Task RefreshReadinessAsync()
    {
        var snapshot = await _systemReadinessService.GetSnapshotAsync(await _settingsService.LoadAsync());
        ReplaceCollection(ServiceReadinessChecks, snapshot.Checks);
        ReplaceCollection(ServiceDiagnostics, snapshot.Diagnostics);
    }

    private async Task RefreshSelectedDeviceAvailabilityAsync()
    {
        if (SelectedDevice is null)
        {
            SelectedDeviceAvailabilityDetail = new DeviceAvailabilityDetail();
            ReplaceCollection(SelectedDeviceAvailabilityTimeline, Array.Empty<DeviceAvailabilityPeriod>());
            return;
        }

        var (startUtc, endUtc) = ResolveTimelineRange();
        var summary30 = (await _availabilityService.GetAvailabilitySummaryAsync(DateTime.UtcNow.AddDays(-30), DateTime.UtcNow, TimeZoneInfo.Local.Id, includeDeleted: true))
            .FirstOrDefault(item => item.DeviceId == SelectedDevice.Id);
        var summary7 = (await _availabilityService.GetAvailabilitySummaryAsync(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, TimeZoneInfo.Local.Id, includeDeleted: true))
            .FirstOrDefault(item => item.DeviceId == SelectedDevice.Id);
        var summary24 = (await _availabilityService.GetAvailabilitySummaryAsync(DateTime.UtcNow.AddHours(-24), DateTime.UtcNow, TimeZoneInfo.Local.Id, includeDeleted: true))
            .FirstOrDefault(item => item.DeviceId == SelectedDevice.Id);
        var timeline = await _availabilityService.GetTimelineAsync(SelectedDevice.Id, startUtc, endUtc);
        var current = timeline.Where(period => !period.EndedAtUtc.HasValue).OrderByDescending(period => period.StartedAtUtc).FirstOrDefault();
        var firstDown = timeline.Where(period => period.Status == AvailabilityStatus.Down).OrderByDescending(period => period.StartedAtUtc).FirstOrDefault();
        var source = summary30 ?? summary7 ?? summary24;

        SelectedDeviceAvailabilityDetail = new DeviceAvailabilityDetail
        {
            DeviceId = SelectedDevice.Id,
            DeviceName = SelectedDevice.Name,
            CurrentStatus = source?.CurrentStatusText ?? SelectedDevice.LastStatusText,
            CurrentStatusSince = FormatLocal(source?.CurrentStatusSinceUtc),
            CurrentContinuousAvailability = source?.CurrentContinuousAvailabilityText ?? "-",
            OngoingDowntime = current?.Status == AvailabilityStatus.Down
                ? AvailabilitySummaryReportItem.FormatDuration((long)(DateTime.UtcNow - current.StartedAtUtc).TotalSeconds)
                : "-",
            FirstFailureAt = FormatLocal(firstDown?.FirstFailureAtUtc),
            ConfirmedDownAt = FormatLocal(firstDown?.ConfirmedAtUtc),
            LastSuccessfulCheck = SelectedDevice.LastSuccessfulCheckAtText,
            LastCheck = SelectedDevice.LastCheckedAtText,
            Availability24Hours = summary24?.AvailabilityText ?? "-",
            Availability7Days = summary7?.AvailabilityText ?? "-",
            Availability30Days = summary30?.AvailabilityText ?? "-",
            Coverage = summary30?.CoverageText ?? "-",
            IncidentCount = (summary30?.IncidentCount ?? 0).ToString(CultureInfo.CurrentCulture),
            Mttr = summary30 is null ? "-" : AvailabilitySummaryReportItem.FormatDuration(summary30.MttrSeconds),
            Mtbf = summary30 is null ? "-" : AvailabilitySummaryReportItem.FormatDuration(summary30.MtbfSeconds),
            SlaTarget = summary30?.SlaTargetPercent is null ? "-" : $"{summary30.SlaTargetPercent:0.###}%",
            SlaStatus = summary30?.SlaStatus ?? "-"
        };
        ReplaceCollection(SelectedDeviceAvailabilityTimeline, timeline.OrderByDescending(period => period.StartedAtUtc));
    }

    private async Task SaveMaintenanceWindowAsync()
    {
        if (string.IsNullOrWhiteSpace(MaintenanceFormName) || !MaintenanceFormStart.HasValue || !MaintenanceFormEnd.HasValue || MaintenanceFormEnd <= MaintenanceFormStart)
        {
            _dialogService.ShowWarning("Bakım geçersiz", "Ad, başlangıç ve bitiş zamanı zorunludur; bitiş başlangıçtan sonra olmalıdır.");
            return;
        }

        var now = DateTime.UtcNow;
        var window = new MaintenanceWindow
        {
            Id = _editingMaintenanceWindowId ?? 0,
            Name = MaintenanceFormName,
            StartedAtUtc = MaintenanceFormStart.Value.ToUniversalTime(),
            EndedAtUtc = MaintenanceFormEnd.Value.ToUniversalTime(),
            Reason = MaintenanceFormReason,
            SuppressNotifications = MaintenanceSuppressNotifications,
            ContinuePings = MaintenanceContinuePings,
            Status = MaintenanceFormStart.Value.ToUniversalTime() <= now && MaintenanceFormEnd.Value.ToUniversalTime() > now
                ? MaintenanceWindowStatus.Active
                : MaintenanceWindowStatus.Scheduled,
            CreatedBy = Environment.UserName,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var targets = new[]
        {
            new MaintenanceWindowTarget
            {
                TargetType = MaintenanceFormTargetType,
                TargetId = MaintenanceFormTargetType == MonitoringTargetType.AllDevices ? null : MaintenanceFormTargetId
            }
        };

        IsBusy = true;
        try
        {
            if (_editingMaintenanceWindowId.HasValue)
            {
                await _maintenanceWindowRepository.UpdateAsync(window, targets);
            }
            else
            {
                await _maintenanceWindowRepository.AddAsync(window, targets);
            }

            ClearMaintenanceWindowForm();
            await LoadMaintenanceWindowsAsync();
            StatusMessage = "Bakım penceresi kaydedildi.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void StartEditMaintenanceWindow()
    {
        if (SelectedMaintenanceWindow is null)
        {
            return;
        }

        var window = SelectedMaintenanceWindow.Window;
        var target = SelectedMaintenanceWindow.Targets.FirstOrDefault();
        _editingMaintenanceWindowId = window.Id;
        MaintenanceFormName = window.Name;
        MaintenanceFormStart = window.StartedAtUtc.ToLocalTime();
        MaintenanceFormEnd = window.EndedAtUtc.ToLocalTime();
        MaintenanceFormReason = window.Reason;
        MaintenanceSuppressNotifications = window.SuppressNotifications;
        MaintenanceContinuePings = window.ContinuePings;
        MaintenanceFormTargetType = target?.TargetType ?? MonitoringTargetType.AllDevices;
        MaintenanceFormTargetId = target?.TargetId;
        OnPropertyChanged(nameof(MaintenanceFormActionText));
    }

    private void ClearMaintenanceWindowForm()
    {
        _editingMaintenanceWindowId = null;
        MaintenanceFormName = string.Empty;
        MaintenanceFormStart = DateTime.Now;
        MaintenanceFormEnd = DateTime.Now.AddHours(1);
        MaintenanceFormReason = string.Empty;
        MaintenanceFormTargetType = MonitoringTargetType.AllDevices;
        MaintenanceFormTargetId = null;
        MaintenanceSuppressNotifications = true;
        MaintenanceContinuePings = true;
        OnPropertyChanged(nameof(MaintenanceFormActionText));
    }

    private async Task CancelSelectedMaintenanceWindowAsync()
    {
        if (SelectedMaintenanceWindow is null)
        {
            return;
        }

        await _maintenanceWindowRepository.CancelAsync(SelectedMaintenanceWindow.Window.Id);
        await LoadMaintenanceWindowsAsync();
    }

    private async Task CompleteSelectedMaintenanceWindowAsync()
    {
        if (SelectedMaintenanceWindow is null)
        {
            return;
        }

        await _maintenanceWindowRepository.CompleteAsync(SelectedMaintenanceWindow.Window.Id);
        await LoadMaintenanceWindowsAsync();
    }

    private async Task SaveMonitoringCalendarAsync()
    {
        if (string.IsNullOrWhiteSpace(CalendarFormName))
        {
            _dialogService.ShowWarning("Takvim geçersiz", "Takvim adı zorunludur.");
            return;
        }

        var calendar = new MonitoringCalendar
        {
            Id = _editingCalendarId ?? 0,
            Name = CalendarFormName,
            TimezoneId = CalendarTimezoneId,
            IsDefault = CalendarIsDefault,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        var rules = CalendarIs24x7 ? Array.Empty<MonitoringCalendarRule>() : BuildCalendarRules();
        var assignments = new[]
        {
            new DeviceMonitoringCalendarAssignment
            {
                TargetType = CalendarAssignmentTargetType,
                TargetId = CalendarAssignmentTargetType == MonitoringTargetType.AllDevices ? null : CalendarAssignmentTargetId,
                CreatedAtUtc = DateTime.UtcNow
            }
        };

        IsBusy = true;
        try
        {
            if (_editingCalendarId.HasValue)
            {
                await _monitoringCalendarRepository.UpdateAsync(calendar, rules, assignments);
            }
            else
            {
                await _monitoringCalendarRepository.AddAsync(calendar, rules, assignments);
            }

            ClearMonitoringCalendarForm();
            await LoadMonitoringCalendarsAsync();
            StatusMessage = "İzleme takvimi kaydedildi.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void StartEditMonitoringCalendar()
    {
        if (SelectedMonitoringCalendar is null)
        {
            return;
        }

        var calendar = SelectedMonitoringCalendar.Calendar;
        var firstRule = SelectedMonitoringCalendar.Rules.FirstOrDefault();
        var firstAssignment = SelectedMonitoringCalendar.Assignments.FirstOrDefault();
        _editingCalendarId = calendar.Id;
        CalendarFormName = calendar.Name;
        CalendarTimezoneId = calendar.TimezoneId;
        CalendarIsDefault = calendar.IsDefault;
        CalendarIs24x7 = SelectedMonitoringCalendar.Rules.Count == 0;
        CalendarStartTime = firstRule?.StartTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture) ?? "08:00";
        CalendarEndTime = firstRule?.EndTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture) ?? "18:00";
        CalendarMonday = HasRule(DayOfWeek.Monday);
        CalendarTuesday = HasRule(DayOfWeek.Tuesday);
        CalendarWednesday = HasRule(DayOfWeek.Wednesday);
        CalendarThursday = HasRule(DayOfWeek.Thursday);
        CalendarFriday = HasRule(DayOfWeek.Friday);
        CalendarSaturday = HasRule(DayOfWeek.Saturday);
        CalendarSunday = HasRule(DayOfWeek.Sunday);
        CalendarAssignmentTargetType = firstAssignment?.TargetType ?? MonitoringTargetType.AllDevices;
        CalendarAssignmentTargetId = firstAssignment?.TargetId;
        OnPropertyChanged(nameof(CalendarFormActionText));

        bool HasRule(DayOfWeek day) => SelectedMonitoringCalendar.Rules.Any(rule => rule.DayOfWeek == day && rule.IsEnabled);
    }

    private void ClearMonitoringCalendarForm()
    {
        _editingCalendarId = null;
        CalendarFormName = string.Empty;
        CalendarTimezoneId = TimeZoneInfo.Local.Id;
        CalendarIsDefault = false;
        CalendarIs24x7 = true;
        CalendarMonday = CalendarTuesday = CalendarWednesday = CalendarThursday = CalendarFriday = true;
        CalendarSaturday = CalendarSunday = true;
        CalendarStartTime = "08:00";
        CalendarEndTime = "18:00";
        CalendarAssignmentTargetType = MonitoringTargetType.AllDevices;
        CalendarAssignmentTargetId = null;
        OnPropertyChanged(nameof(CalendarFormActionText));
    }

    private async Task DeleteSelectedMonitoringCalendarAsync()
    {
        if (SelectedMonitoringCalendar is null)
        {
            return;
        }

        await _monitoringCalendarRepository.DeleteAsync(SelectedMonitoringCalendar.Calendar.Id);
        await LoadMonitoringCalendarsAsync();
    }

    private IReadOnlyList<MonitoringCalendarRule> BuildCalendarRules()
    {
        if (!TimeSpan.TryParse(CalendarStartTime, CultureInfo.InvariantCulture, out var start)
            || !TimeSpan.TryParse(CalendarEndTime, CultureInfo.InvariantCulture, out var end))
        {
            start = TimeSpan.FromHours(8);
            end = TimeSpan.FromHours(18);
        }

        var days = new List<DayOfWeek>();
        if (CalendarSunday) days.Add(DayOfWeek.Sunday);
        if (CalendarMonday) days.Add(DayOfWeek.Monday);
        if (CalendarTuesday) days.Add(DayOfWeek.Tuesday);
        if (CalendarWednesday) days.Add(DayOfWeek.Wednesday);
        if (CalendarThursday) days.Add(DayOfWeek.Thursday);
        if (CalendarFriday) days.Add(DayOfWeek.Friday);
        if (CalendarSaturday) days.Add(DayOfWeek.Saturday);

        return days.Select(day => new MonitoringCalendarRule
        {
            DayOfWeek = day,
            StartTime = start,
            EndTime = end,
            IsEnabled = true
        }).ToList();
    }

    private (DateTime StartUtc, DateTime EndUtc) ResolveTimelineRange()
    {
        var now = DateTime.UtcNow;
        return SelectedTimelineRange switch
        {
            "Son 24 saat" => (now.AddHours(-24), now),
            "Son 7 gün" => (now.AddDays(-7), now),
            "Bu ay" => (new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1).ToUniversalTime(), now),
            "Özel tarih aralığı" when CustomTimelineStartDate.HasValue && CustomTimelineEndDate.HasValue
                => (CustomTimelineStartDate.Value.ToUniversalTime(), CustomTimelineEndDate.Value.Date.AddDays(1).ToUniversalTime()),
            _ => (now.AddDays(-30), now)
        };
    }
}
