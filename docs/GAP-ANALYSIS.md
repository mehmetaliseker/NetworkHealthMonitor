# Gap Analysis

Last reviewed: 2026-07-31

Status values: Tamamlandi, Kismen tamamlandi, Yalniz UI, Yalniz backend, Mock/placeholder, Eksik, Dogrulanmadi.

| Akis | UI mevcut mu? | ViewModel mevcut mu? | Application service mevcut mu? | Repository mevcut mu? | Veritabani mevcut mu? | Worker entegrasyonu mevcut mu? | Runtime dogrulandi mi? | Durum | Eksik | Onerilen islem |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Ana navigasyon | Evet | `ShellViewModel` | `NavigationService` | Yok | Yok | Yok | UIA + screenshot, 2026-07-31 | Tamamlandi | Yok | Yeni route eklenirse test ekle |
| Sayfa lifecycle | Evet | `PageViewModelBase` | Yok | Yok | Yok | Yok | Unit test | Kismen tamamlandi | Dirty form guard yok | `CanNavigateAwayAsync` ekle |
| Yeni cihaz kaydi | Evet | Legacy facade + `DevicesViewModel` | `DeviceService` | `DeviceRepository` | `Devices` | Sonraki query | UI modal + save, 2026-07-31 | Kismen tamamlandi | Page VM hala legacy state kullaniyor | `DeviceEditorViewModel` cikar |
| Cihaz duzenleme | Evet | Legacy facade | `DeviceService` | `DeviceRepository` | `Devices` | Sonraki query | Dogrulanmadi | Dogrulanmadi | Yeni shell uzerinde edit E2E bekliyor | UI runtime flow |
| Cihaz silme/pasif | Evet | Legacy facade | `DeviceService` | `DeviceRepository` | `Devices`, related tables | Evet | UI soft-delete, 2026-07-31 | Kismen tamamlandi | Pasife alma ve acik incident metni runtime dogrulanmadi | Akis testi + copy |
| Tek ping | Evet | Legacy facade | `PingExecutionService` | ping/device/outage/incident repos | `PingLogs`, `Devices`, `Outages`, `DeviceIncidents` | Paylasilan DB | UI + SQLite, 2026-07-31 | Tamamlandi | Page VM strangler ayrimi devam ediyor | Legacy facade azalt |
| Toplu ping | Evet | Legacy facade | `PingExecutionService` | same | same | Paylasilan DB | Dashboard UI + SQLite, 2026-07-31 | Tamamlandi | Selected/group direct UI ayrintili smoke genisletilebilir | Page-level progress devices/groups sayfalarina da tasinabilir |
| Scheduler | Evet | Legacy facade | `SchedulerService`, `SchedulePlanService` | `SchedulePlanRepository` | `SchedulePlans` | Evet | Dogrulanmadi | Kismen tamamlandi | Plan create/run E2E bekliyor | Runtime scheduler smoke |
| Incident | Evet | Legacy facade | `IncidentService` | `OutageRepository`, SQLite incident commands | `Outages`, `DeviceIncidents` | Evet | Dogrulanmadi | Kismen tamamlandi | Retry detail view sinirli | Incident detail VM |
| Notification history | Evet | Legacy facade | `NotificationDispatcherService` | `NotificationOutboxRepository` | `NotificationOutbox` | Evet | Dogrulanmadi | Kismen tamamlandi | Test mode runtime yok | Fake/local channel smoke |
| Notification rules | Evet | Legacy facade | `AppSettingsService` | Settings storage | `AppSettings` | Worker reads settings | Dogrulanmadi | Kismen tamamlandi | Rule dialog not wired | Dedicated rule VM/dialog |
| Cihaz Gruplari | Evet | `DeviceGroupsViewModel` + legacy | `DeviceGroupService` | `DeviceGroupRepository` | `DeviceGroups`, `Devices` | Targets read by scheduler | Dogrulanmadi | Kismen tamamlandi | Group edit not on new page | Group management VM |
| Canli Durum | Evet | `LiveStatusViewModel` + legacy | Projection from loaded data | Device/ping repositories through refresh | `Devices`, `PingLogs` | Shared DB | Dogrulanmadi | Kismen tamamlandi | Not real-time polling page lifecycle | Timer lifecycle |
| Raporlar | Evet | `ReportsViewModel` + legacy | `AvailabilityService`, `PingLogRepository`, `CsvExportService` | `AvailabilityRepository`, `PingLogRepository` | availability tables, `PingLogs` | Worker writes data | Uptime CSV runtime, 2026-07-31 | Kismen tamamlandi | SLA/MTTR/ping performance UI raporlari ayri E2E kanit bekliyor | Report-specific use cases |
| Worker Servisi | Evet | `WorkerServiceViewModel` + legacy | `IWindowsServiceStatusService` | `WorkerHeartbeatRepository` | `WorkerHeartbeat` | SCM/worker process | UI start + SCM readback, 2026-07-31 | Kismen tamamlandi | Elevation dialog not wired; start button remains visible while running | Dedicated service VM |
| System health | Evet | `SystemHealthViewModel` + legacy | readiness/heartbeat/settings services | heartbeat/repositories | `WorkerHeartbeat`, DB file | Evet | Screenshot + heartbeat, 2026-07-31 | Kismen tamamlandi | Scheduler pending jobs partial | Health service facade |
| Backup/restore | Evet | Legacy facade | `DataMaintenanceService` | File/SQLite | DB file | Hayir | Backup real runtime + restore isolated, 2026-07-31 | Kismen tamamlandi | Worker safe stop/resume ve primary DB restore UI testi yok | Restore orchestration |
| CSV import/export | Evet | Legacy facade | `DeviceImportExportService`, `CsvExportService` | `DeviceRepository` | `Devices`, `CsvImportAudits` | Sonraki query | Runtime service path, 2026-07-31 | Kismen tamamlandi | OS file dialog manuel kaniti yok | File dialog UI smoke + sample CSV |
| DPI/screenshots | Evet | Yok | `ApplicationHighDpiMode=PerMonitorV2` | Yok | Yok | Yok | 1366x768 + max visible screenshot | Kismen tamamlandi | 1920x1080, %125, %150 real OS scale dogrulanmadi | Dedicated DPI test host |

## Specific Checks

- Cihaz Gruplari real table: `DeviceGroupRepository` uses `DeviceGroups`; current page summary uses loaded real groups. Runtime not yet verified in new shell.
- Canli Durum real ping results: uses `Devices` last status and `PingLogs`; live polling lifecycle not yet implemented.
- Raporlar real calculations: availability grid uses `AvailabilityService`; all named cards do not yet have independent report calculators.
- Bildirim Kurallari saved: rule controls bind to `AppSettingsService` via `SaveSettingsCommand`; dedicated rule dialog not wired.
- Sistem Sagligi scheduler status: shows scheduler/heartbeat data, pending job count is approximate.
- Worker Servisi SCM status: `IWindowsServiceStatusService` uses `sc.exe` and readback; runtime pass pending.
- Backup/restore buttons: real `DataMaintenanceService` commands exist; safe worker stop around restore missing.
- Ayarlar persistence: core settings save to `AppSettings`; theme is stored but not fully applied as a visual theme.
- CSV dialogs: `WpfDialogService` has real dialogs; new shell runtime pass pending.
- Report CSV export: `CsvExportService` is real; report-specific exports are shared availability exports.
- Device detail tabs: filtered data comes from legacy projections over loaded collections.
- Incident detail retry/notifications: retry count appears; full notification history per incident is not fully surfaced.

## Runtime Notes - 2026-07-31

- Screenshot artifact directory: `artifacts/ui-validation-20260731-195540`.
- Captured screens: Dashboard, Devices, Add Device modal, Device Details, Live Status, Incidents, Notifications, Worker Service, System Health, Settings.
- Captured sizes: `1366x768` and current host maximum logical area `1707x1019`; `1920x1080` was not fully visible in this session.
- A temporary device `NHM Runtime Test 20260731 2000` was created through the UI, opened in Device Details, then removed through the UI delete flow. The application delete behavior is soft-delete, so active lists are clean while historical DB semantics are preserved.
- Worker Service was started from the Worker page and read back from SCM as `Running` / `Manual`.
