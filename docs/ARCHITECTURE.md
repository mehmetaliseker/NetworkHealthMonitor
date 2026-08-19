# NetworkHealthMonitor Architecture

Last reviewed: 2026-07-31

## Current Layers

```text
WPF View
  -> ShellViewModel / page ViewModel
  -> legacy MainViewModel facade for existing commands
  -> application services
  -> repository classes
  -> SqliteConnectionFactory
  -> SQLite database
```

The UI has been split into physical page views and page ViewModels. Existing business behavior is still preserved through `MainViewModel` while the page state is being moved behind smaller ViewModel surfaces. This is intentional transitional architecture: P0/P1 behavior remains wired while the large legacy facade is reduced incrementally.

## Composition Root

`MainWindow.xaml.cs` creates the SQLite connection factory, repositories, application services, scheduler, ping execution service, notification publisher/client and the legacy `MainViewModel`. It then creates `ShellViewModel` and assigns it as the `DataContext`.

`MainWindow.xaml` is now a shell host only. It contains global resources, ViewModel-to-View DataTemplates, and `Views/Shell/MainShellView.xaml`.

## UI Modules

| Area | View | ViewModel | State owner today | Main services used |
| --- | --- | --- | --- | --- |
| Shell | `Views/Shell/MainShellView.xaml` | `ShellViewModel` | Shell | `NavigationService` |
| Navigation | `Views/Shell/SidebarNavigation.xaml` | `ShellViewModel` | Shell | `NavigationService` |
| Dashboard | `Views/Dashboard/DashboardView.xaml` | `DashboardViewModel` | Page + legacy facade | device/log/outage/heartbeat projections |
| Devices | `Views/Devices/DevicesView.xaml` | `DevicesViewModel` | Page + legacy facade | `IDeviceService`, `DeviceRepository`, CSV services |
| Device details | `Views/Devices/DeviceDetailsView.xaml` | `DeviceDetailsViewModel` | Page + legacy facade | filtered ping/outage/outbox projections |
| Groups | `Views/Devices/DeviceGroupsView.xaml` | `DeviceGroupsViewModel` | Page + legacy facade | `IDeviceGroupService`, `DeviceGroupRepository` |
| Live status | `Views/LiveStatus/LiveStatusView.xaml` | `LiveStatusViewModel` | Page + legacy facade | device and ping projections |
| Schedules | `Views/Schedules/SchedulesView.xaml` | `SchedulesViewModel` | Page + legacy facade | `ISchedulePlanService`, `ISchedulerService` |
| Ping history | `Views/PingHistory/PingHistoryView.xaml` | `PingHistoryViewModel` | Page + legacy facade | `PingLogRepository`, `CsvExportService` |
| Incidents | `Views/Incidents/IncidentsView.xaml` | `IncidentsViewModel` | Page + legacy facade | `OutageRepository`, `AvailabilityRepository`, `IncidentService` |
| Notifications | `Views/Notifications/NotificationsView.xaml` | `NotificationsViewModel` | Page + legacy facade | `NotificationOutboxRepository`, ntfy, SMTP |
| Reports | `Views/Reports/ReportsView.xaml` | `ReportsViewModel` | Page + legacy facade | `AvailabilityService`, `CsvExportService` |
| Worker | `Views/Worker/WorkerServiceView.xaml` | `WorkerServiceViewModel` | Page + legacy facade | `IWindowsServiceStatusService`, `WorkerHeartbeatRepository` |
| System health | `Views/SystemHealth/SystemHealthView.xaml` | `SystemHealthViewModel` | Page + legacy facade | heartbeat, readiness, SQLite status |
| Settings | `Views/Settings/SettingsView.xaml` | `SettingsViewModel` | Page + legacy facade | `AppSettingsService`, `DataMaintenanceService` |

## Navigation

`INavigationService` and `NavigationService` own the active page object and bounded back stack. Page ViewModels implement `INavigationAware` and `IRefreshable` through `PageViewModelBase`.

Navigation route example:

```text
ShellViewModel.NavigateToAsync<DevicesViewModel>()
  -> NavigationService.NavigateAsync<DevicesViewModel>()
  -> DevicesViewModel.OnNavigatedFrom/To lifecycle
  -> ContentControl renders DevicesView via DataTemplate
```

Device details route:

```text
DevicesView row command
  -> ShellViewModel.OpenDeviceDetailsCommand
  -> ShellViewModel.NavigateToAsync<DeviceDetailsViewModel>(Device)
  -> DeviceDetailsViewModel applies selected device to legacy facade
  -> DeviceDetailsView renders scoped tabs
```

## Page Lifecycle

`PageViewModelBase` exposes:

```csharp
bool IsLoading
bool HasData
bool HasError
string? ErrorMessage
ICommand RetryCommand
ICommand RefreshCommand
Task LoadAsync(CancellationToken)
```

Rules implemented now:

- First navigation loads the page.
- Simple return navigation reuses the cached page instance.
- Explicit refresh reloads page state.
- `OnNavigatedFromAsync` cancels the page navigation token.
- Device details reloads when a new device parameter is supplied.

Remaining transition: many page ViewModels delegate data collections and commands to `MainViewModel` until each use case facade is extracted.

## Application Services

| Feature | Service | Repository / infrastructure | Tables |
| --- | --- | --- | --- |
| Device CRUD | `IDeviceService` / `DeviceService` | `DeviceRepository` | `Devices`, `DeviceGroups`, `DeviceAvailabilityPeriods` |
| Device groups | `IDeviceGroupService` / `DeviceGroupService` | `DeviceGroupRepository` | `DeviceGroups`, `Devices` |
| Ping execution | `IPingExecutionService` / `PingExecutionService` | `IPingService`, `PingLogRepository`, `DeviceRepository`, `OutageRepository` | `PingLogs`, `Devices`, `Outages`, `DeviceIncidents`, `DeviceAvailabilityPeriods` |
| Scheduler | `ISchedulerService` / `SchedulerService` | `SchedulePlanRepository`, `SchedulePlanTargetResolver` | `SchedulePlans`, `PingLogs`, `WorkerHeartbeat` |
| Incident state | `IIncidentService` / `IncidentService` | SQLite commands + `NotificationOutboxRepository` | `DeviceIncidents`, `NotificationOutbox`, `PingLogs` |
| Notification dispatch | `NotificationDispatcherService` | `INotificationOutboxRepository`, `NtfyNotificationChannel`, `EmailNotificationChannel` | `NotificationOutbox`, `DeviceIncidents`, `WorkerHeartbeat` |
| Reports | `IAvailabilityService` / `AvailabilityService` | `AvailabilityRepository`, `CsvExportService` | `DeviceAvailabilityDaily`, `DeviceAvailabilityPeriods`, `DeviceIncidents` |
| Backup/restore | `DataMaintenanceService` | filesystem + SQLite file path | database file and backup folder |
| Settings | `AppSettingsService` | `SqliteConnectionFactory` | `AppSettings` |
| Worker service control | `IWindowsServiceStatusService` | `sc.exe`, elevated PowerShell when required | Windows SCM, not SQLite |
| Worker heartbeat | `WorkerHeartbeatRepository` | `SqliteConnectionFactory` | `WorkerHeartbeat` |

## Worker Runtime

```text
Windows Service
  -> Worker Host
  -> WorkerComposition
  -> WorkerRuntime
  -> SchedulerService
  -> DeviceRepository.GetAutoCheckCandidatesAsync
  -> PingExecutionService
  -> PingService
  -> PingLogRepository / DeviceRepository / OutageRepository
  -> IncidentService
  -> NotificationOutbox
  -> NotificationDispatcherService
  -> ntfy / SMTP
  -> WorkerHeartbeatRepository
```

The worker reads the same SQLite database as the UI. It does not reference the WPF UI assembly. Heartbeat is written to `WorkerHeartbeat`; UI and System Health read it through `WorkerHeartbeatRepository`.

## Tray Runtime

The tray is a separate WPF/WinForms NotifyIcon application. It uses `WindowsWorkerServiceController`, `TrayMenuStateBuilder`, and elevated PowerShell scripts for service installation/start/stop operations. It polls service status every 3 seconds. It does not host the console worker.

## Dependency Rules

Current target:

- Views bind to page ViewModels or the legacy facade exposed by page ViewModels.
- Page ViewModels should call application services or page use cases, not repositories directly.
- UI must not execute SQL directly.
- UI must not use `ServiceController` directly.
- Worker must not reference WPF UI.

Current deviation:

- `MainViewModel` still has direct repository dependencies and page-specific state. It is retained as a compatibility facade while pages are physically split.
- `MainWindow.xaml.cs` is still the manual composition root rather than a DI container module.

## Lifetime Decisions

- Singleton-like: `ShellViewModel`, `NavigationService`, cached page ViewModels, repositories/services created by `MainWindow`.
- Cached pages: Dashboard, Devices, DeviceDetails, Groups, LiveStatus, Schedules, PingHistory, Incidents, Notifications, Reports, Worker, SystemHealth, Settings, Help, About.
- Transient dialogs: the existing device and schedule dialog views are displayed by `GlobalDialogHost`; their state is still held by the legacy facade.
- Worker/Tray: separate process lifetimes.

## DPI and Runtime Validation

- The WPF host uses `ApplicationHighDpiMode=PerMonitorV2` in `NetworkHealthMonitor.csproj`.
- Runtime screenshots were captured at `1366x768` and at the current host maximum logical area (`1707x1019`).
- The current validation machine did not expose a fully visible `1920x1080` logical work area, so that exact size and real `%125/%150` OS scale checks remain unverified.
