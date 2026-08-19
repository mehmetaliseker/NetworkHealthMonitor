# User Flows

Last reviewed: 2026-07-31

## A. First Use

- Baslangic noktasi: `MainWindow` opens and initializes SQLite through `SqliteConnectionFactory.InitializeAsync`.
- Kullanici aksiyonu: no device exists; user opens `Cihazlar` or uses dashboard quick action.
- UI state: Dashboard summary and Devices empty state.
- ViewModel command: `ShellViewModel.NavigateDevicesCommand`, then `MainViewModel.NavigateDeviceEditCommand`.
- Application service: `DeviceService`.
- Repository/veritabani: `DeviceRepository` writes `Devices`.
- Worker etkisi: worker reads new active device on next scheduler candidate query.
- Basari sonucu: device modal closes and list refreshes.
- Hata sonucu: validation message stays in modal; raw exception is not shown.
- Iptal sonucu: modal closes through `CloseDeviceFormCommand`.
- Eksik durum: onboarding is an empty-state action, not a separate guided wizard.

## B. Manual Device Add

- Baslangic noktasi: `DevicesView`.
- Kullanici aksiyonu: `+ Yeni Cihaz`, fill modal, optional connection test, save.
- UI state: `AddEditDeviceDialog` in `GlobalDialogHost`.
- ViewModel command: `NavigateDeviceEditCommand`, `TestDeviceConnectionCommand`, `SaveDeviceCommand`.
- Application service: `DeviceConnectionTestService`, `DeviceService`.
- Repository/veritabani: `DeviceRepository.AddAsync` writes `Devices`.
- Worker etkisi: visible on next `GetAutoCheckCandidatesAsync`.
- Basari sonucu: modal closes, `Devices` collection reloads.
- Hata sonucu: name/address validation or user-safe service error.
- Iptal sonucu: form state clears and modal closes.
- Eksik durum: runtime save was verified on 2026-07-31 with a temporary device; a dedicated `DeviceEditorViewModel` is still missing.

## C. Device Edit

- Baslangic noktasi: `DeviceDetailsView` or Devices row.
- Kullanici aksiyonu: `Duzenle`.
- UI state: same modal opens with selected device data.
- ViewModel command: `EditSelectedDeviceCommand` or `EditDeviceCommand`, then `SaveDeviceCommand`.
- Application service: `DeviceService`.
- Repository/veritabani: `DeviceRepository.UpdateAsync` updates `Devices`.
- Worker etkisi: worker uses updated timeout/group/active values on next query.
- Basari sonucu: modal closes and page projections refresh.
- Hata sonucu: validation/service message.
- Iptal sonucu: old persisted device remains unchanged.
- Eksik durum: dirty-form navigation warning is not implemented.

## D. Delete or Deactivate Device

- Baslangic noktasi: Devices row or selection.
- Kullanici aksiyonu: `Sil`, `Pasif Yap`, or bulk commands.
- UI state: legacy `IDialogService.Confirm` confirmation for destructive commands.
- ViewModel command: `DeleteDeviceCommand`, `DeleteSelectedDevicesBulkCommand`, `DeactivateSelectedDevicesCommand`.
- Application service: `DeviceService`.
- Repository/veritabani: soft delete/update in `Devices`; related incidents/outages/outbox are updated by repository logic.
- Worker etkisi: deleted/inactive devices are filtered out.
- Basari sonucu: list refreshes. Runtime soft-delete was verified on 2026-07-31 with a temporary device.
- Hata sonucu: user-safe warning/error dialog.
- Iptal sonucu: no DB write.
- Eksik durum: delete vs deactivate explanatory screen copy can be stronger.

## E. Single Device Check

- Baslangic noktasi: Devices row, Device details settings tab.
- Kullanici aksiyonu: `Ping` / `Simdi Kontrol Et`.
- UI state: busy/progress summary in global status.
- ViewModel command: `PingDeviceCommand` or `PingSelectedDeviceCommand`.
- Application service: `PingExecutionService`.
- Repository/veritabani: writes `PingLogs`, updates `Devices`, `Outages`, `DeviceIncidents`, `DeviceAvailabilityPeriods`.
- Worker etkisi: none directly for manual ping, but DB state is shared.
- Basari sonucu: status, history and incidents refresh.
- Hata sonucu: ping failure is stored as result; execution errors are logged and surfaced safely.
- Iptal sonucu: `CancelPingCommand` cancels active batch token.
- Eksik durum: page-level cancel button is present through legacy state, not yet standardized per page ViewModel. This flow was not executed in the final runtime pass.

## F. Bulk Check

- Baslangic noktasi: Dashboard quick action or Devices filtered selection.
- Kullanici aksiyonu: all/filter/selected ping.
- UI state: busy/progress counters on legacy facade.
- ViewModel command: `PingAllCommand`, `PingFilteredDevicesCommand`, `PingSelectedDevicesBulkCommand`.
- Application service: `PingExecutionService`.
- Repository/veritabani: same as single ping.
- Worker etkisi: manual results share DB with worker.
- Basari sonucu: success/failure summary in status.
- Hata sonucu: safe message + log.
- Iptal sonucu: `CancelPingCommand`.
- Eksik durum: progress UI is still global, not a page-specific progress component.

## G. Scheduler

- Baslangic noktasi: `SchedulesView`.
- Kullanici aksiyonu: create/edit plan or run selected plan.
- UI state: `AddEditScheduleDialog`.
- ViewModel command: `OpenSchedulePlanFormCommand`, `SaveSchedulePlanCommand`, `RunSelectedSchedulePlanCommand`.
- Application service: `SchedulePlanService`, `SchedulerService`.
- Repository/veritabani: `SchedulePlanRepository` writes `SchedulePlans`; run writes ping data.
- Worker etkisi: worker loads active plans and computes next run.
- Basari sonucu: plan list and run status refresh.
- Hata sonucu: validation or scheduler error message.
- Iptal sonucu: modal closes.
- Eksik durum: full create-plan E2E was not yet runtime-verified in this pass.

## H. Incident

- Baslangic noktasi: failed ping from UI or worker.
- Kullanici aksiyonu: none after ping starts.
- UI state: Incidents page shows open/resolved/all.
- ViewModel command: ping/scheduler command triggers incident processing.
- Application service: `IncidentService`, `AvailabilityRepository`.
- Repository/veritabani: `DeviceIncidents`, `Outages`, `NotificationOutbox`, availability periods.
- Worker etkisi: scheduled failures open incidents and queue notifications.
- Basari sonucu: open incident appears and closes on recovery.
- Hata sonucu: errors logged; notification queue failures appear in outbox.
- Iptal sonucu: cancelled ping does not complete incident transition.
- Eksik durum: retry history detail is not fully surfaced in a dedicated incident detail view.

## I. Notification

- Baslangic noktasi: incident domain event or test notification.
- Kullanici aksiyonu: configure rules, test, retry failed outbox.
- UI state: `NotificationsView` history/rules tabs.
- ViewModel command: `SaveSettingsCommand`, `SendTestNotificationCommand`, `TestSmtpConnectionCommand`, `RetryOutboxCommand`.
- Application service: `NotificationPublisher`, `NotificationDispatcherService`.
- Repository/veritabani: `NotificationOutbox`.
- Worker etkisi: dispatcher claims due rows and sends through ntfy/SMTP.
- Basari sonucu: outbox row moves to sent.
- Hata sonucu: retry/dead-letter status with safe error.
- Iptal sonucu: cancel pending outbox command exists.
- Eksik durum: notification rule dialog file exists but rules are still edited on page forms.

## J. Worker Management

- Baslangic noktasi: Worker Service page or Tray.
- Kullanici aksiyonu: start, stop, restart, install, uninstall.
- UI state: operation message and readback status.
- ViewModel command: `StartSchedulerCommand`, `StopSchedulerCommand`, `RestartWorkerServiceCommand`, `InstallWorkerServiceCommand`, `UninstallWorkerServiceCommand`.
- Application service: `IWindowsServiceStatusService`.
- Repository/veritabani: heartbeat read via `WorkerHeartbeatRepository`; SCM is source for service state.
- Worker etkisi: service starts/stops worker process.
- Basari sonucu: UI/tray poll real SCM state. Runtime start was verified on 2026-07-31; SCM readback was `Running` and `Manual`.
- Hata sonucu: access denied launches elevated flow or returns safe failure message.
- Iptal sonucu: UAC cancellation returns failure message.
- Eksik durum: central elevation dialog view exists but current implementation uses OS UAC directly.

## K. Worker Auto Startup

- Baslangic noktasi: Worker Service page checkbox.
- Kullanici aksiyonu: change checked state.
- UI state: operation-in-progress, then SCM readback.
- ViewModel command: `WorkerAutostartEnabled` setter.
- Application service: `IWindowsServiceStatusService.SetStartupTypeAsync`.
- Repository/veritabani: none.
- Worker etkisi: Windows service startup type changes.
- Basari sonucu: `Manual`/`Automatic` shown from SCM readback.
- Hata sonucu: checkbox reverts to readback value and message is shown.
- Iptal sonucu: UAC cancellation leaves SCM unchanged.
- Eksik durum: not changed during this work; required final state remains Manual.

## L. Backup and Restore

- Baslangic noktasi: Settings > Veri ve Yedekleme.
- Kullanici aksiyonu: backup or restore.
- UI state: file dialogs from `WpfDialogService`.
- ViewModel command: `BackupDatabaseCommand`, `RestoreDatabaseCommand`.
- Application service: `DataMaintenanceService`.
- Repository/veritabani: SQLite file copy and restore path validation.
- Worker etkisi: not orchestrated by UI.
- Basari sonucu: backup path or restore success message.
- Hata sonucu: safe dialog and logged exception.
- Iptal sonucu: file dialog cancellation does nothing.
- Eksik durum: safe worker stop/restore/resume flow is not implemented.

## M. Error Recovery

| Situation | User result today | Gap |
| --- | --- | --- |
| SQLite cannot open | startup error dialog with DB/log path | recovery wizard missing |
| Worker not installed | Worker page/tray shows not installed | install path exists |
| Worker cannot start | safe operation failure | detailed remediation not centralized |
| Ping service error | status/log message | page error state not always specific |
| Scheduler stopped | Worker/Scheduler status text | restart guidance limited |
| Notification channel fails | outbox retry/dead-letter | rule validation can be clearer |
| Migration fails | startup error + log | rollback UI missing |
| Database locked | logged; heartbeat may record DB lock | user flow not explicit |
| Config corrupt | settings load falls back where possible | repair UI missing |
| Permission denied | UAC/elevated flow or failure message | central elevation dialog not wired |
