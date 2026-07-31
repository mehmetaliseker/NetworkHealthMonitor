# UI Navigation Map

Last reviewed: 2026-07-31

## Shell

`MainWindow.xaml` hosts `Views/Shell/MainShellView.xaml`.

```text
SidebarNavigation
TopBar
ContentControl(CurrentPage)
GlobalDialogHost
ToastHost
Bottom status bar
```

All primary navigation buttons have visible text, `AutomationProperties.Name`, and `ToolTip`.

## Primary Routes

| Navigation label | Command | Current page ViewModel | View | Purpose |
| --- | --- | --- | --- | --- |
| Genel Bakis | `ShellViewModel.NavigateDashboardCommand` | `DashboardViewModel` | `Views/Dashboard/DashboardView.xaml` | Summary cards, recent alerts, quick actions |
| Cihazlar | `NavigateDevicesCommand` | `DevicesViewModel` | `Views/Devices/DevicesView.xaml` | Device CRUD and CSV actions |
| Cihaz Gruplari | `NavigateGroupsCommand` | `DeviceGroupsViewModel` | `Views/Devices/DeviceGroupsView.xaml` | Group summary and group devices |
| Canli Durum | `NavigateLiveStatusCommand` | `LiveStatusViewModel` | `Views/LiveStatus/LiveStatusView.xaml` | Online/offline/checked status views |
| Kontrol Planlari | `NavigateSchedulesCommand` | `SchedulesViewModel` | `Views/Schedules/SchedulesView.xaml` | Schedule plans and retry rules |
| Ping Gecmisi | `NavigateLogsCommand` | `PingHistoryViewModel` | `Views/PingHistory/PingHistoryView.xaml` | Ping log filtering and export |
| Kesintiler | `NavigateEventsCommand` | `IncidentsViewModel` | `Views/Incidents/IncidentsView.xaml` | Open/resolved/all outages |
| Bildirimler | `NavigateNotificationsCommand` | `NotificationsViewModel` | `Views/Notifications/NotificationsView.xaml` | Outbox history and notification rules |
| Raporlar | `NavigateReportsCommand` | `ReportsViewModel` | `Views/Reports/ReportsView.xaml` | Availability/SLA/report export |
| Worker Servisi | `NavigateWorkerServiceCommand` | `WorkerServiceViewModel` | `Views/Worker/WorkerServiceView.xaml` | SCM status/start/stop/startup type |
| Sistem Sagligi | `NavigateSystemHealthCommand` | `SystemHealthViewModel` | `Views/SystemHealth/SystemHealthView.xaml` | UI/worker/tray/SQLite/scheduler/log health |
| Ayarlar | `NavigateSettingsCommand` | `SettingsViewModel` | `Views/Settings/SettingsView.xaml` | General/control/notification/data/appearance/advanced settings |

## Secondary Routes

| Navigation label | Command | ViewModel | View |
| --- | --- | --- | --- |
| Yardim | `NavigateHelpCommand` | `HelpViewModel` | `Views/Help/HelpView.xaml` |
| Hakkinda | `NavigateAboutCommand` | `AboutViewModel` | `Views/About/AboutView.xaml` |

## Child Routes and Tabs

| Parent | Child/tab | View |
| --- | --- | --- |
| Device details | Genel | `Views/Devices/Tabs/DeviceGeneralTab.xaml` |
| Device details | Kontrol Gecmisi | `Views/Devices/Tabs/DevicePingHistoryTab.xaml` |
| Device details | Kesintiler | `Views/Devices/Tabs/DeviceIncidentsTab.xaml` |
| Device details | Bildirimler | `Views/Devices/Tabs/DeviceNotificationsTab.xaml` |
| Device details | Ayarlar | `Views/Devices/Tabs/DeviceSettingsTab.xaml` |
| System health | Genel Saglik | `Views/SystemHealth/Tabs/GeneralHealthTab.xaml` |
| System health | Veritabani | `Views/SystemHealth/Tabs/DatabaseHealthTab.xaml` |
| System health | Scheduler | `Views/SystemHealth/Tabs/SchedulerHealthTab.xaml` |
| System health | Loglar | `Views/SystemHealth/Tabs/LogsTab.xaml` |

## Breadcrumb Rules

- Normal pages use page title.
- Device details uses `Cihazlar > {Device.Name}`.
- Back navigation is bounded by `NavigationService` history depth.

