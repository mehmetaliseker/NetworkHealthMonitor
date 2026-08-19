# Data Flows

Last reviewed: 2026-07-31

## Device CRUD

```mermaid
flowchart LR
    UI[DevicesView / AddEditDeviceDialog]
    PVM[DevicesViewModel]
    LEG[MainViewModel compatibility facade]
    AS[DeviceService]
    R[DeviceRepository]
    DB[(SQLite: Devices, DeviceGroups)]
    W[Worker Scheduler]

    UI --> PVM --> LEG --> AS --> R --> DB
    W --> R
```

Save success closes the modal, reloads device collections, and the worker sees active devices on the next `GetAutoCheckCandidatesAsync` query. Validation errors remain in the modal through user-safe validation messages.

## Ping and Incident

```mermaid
flowchart LR
    UI[Devices / LiveStatus / Schedules]
    LEG[MainViewModel]
    PES[PingExecutionService]
    PS[PingService]
    PLR[PingLogRepository]
    DR[DeviceRepository]
    OR[OutageRepository]
    IS[IncidentService]
    AR[AvailabilityRepository]
    DB[(SQLite)]

    UI --> LEG --> PES --> PS
    PES --> PLR --> DB
    PES --> DR --> DB
    PES --> OR --> DB
    PES --> IS --> DB
    PES --> AR --> DB
```

`PingExecutionService` filters inactive, disabled, deleted and scheduled-paused devices before pinging.

## Notification Outbox

```mermaid
flowchart LR
    IS[IncidentService]
    OUT[NotificationOutboxRepository]
    DB[(SQLite: NotificationOutbox)]
    DISP[NotificationDispatcherService]
    NTFY[NtfyNotificationChannel]
    SMTP[EmailNotificationChannel]
    HB[WorkerHeartbeatRepository]

    IS --> OUT --> DB
    DISP --> OUT
    DISP --> NTFY
    DISP --> SMTP
    DISP --> HB
```

History screens read `NotificationOutbox`; they are not based only on log text.

## Scheduler

```mermaid
flowchart LR
    W[WorkerService]
    RT[WorkerRuntime]
    S[SchedulerService]
    SPR[SchedulePlanRepository]
    DQ[DeviceRepository]
    TR[SchedulePlanTargetResolver]
    PES[PingExecutionService]
    DB[(SQLite)]

    W --> RT --> S
    S --> SPR --> DB
    S --> DQ --> DB
    S --> TR
    S --> PES
```

The UI can request plan run/start/stop through existing commands. The real recurring execution path is the worker-hosted scheduler.

## Worker Service Control

```mermaid
flowchart LR
    UI[WorkerServiceView / Tray]
    VM[WorkerServiceViewModel or Tray controller]
    SCM[IWindowsServiceStatusService / WindowsWorkerServiceController]
    ELEV[Elevated PowerShell when required]
    WIN[Windows Service Control Manager]

    UI --> VM --> SCM
    SCM --> WIN
    SCM --> ELEV --> WIN
```

Startup type is read back from SCM. `Manual` maps to `DEMAND_START`; `Automatic` maps to `AUTO_START`.

## Backup and Restore

```mermaid
flowchart LR
    UI[SettingsView]
    LEG[MainViewModel]
    DM[DataMaintenanceService]
    FS[Filesystem]
    DB[(SQLite file)]

    UI --> LEG --> DM
    DM --> FS
    DM --> DB
```

Restore creates a pre-restore backup and replaces the SQLite file. Safe worker stop/resume orchestration is not fully implemented in UI.

## Reporting

```mermaid
flowchart LR
    UI[ReportsView]
    LEG[MainViewModel]
    AS[AvailabilityService]
    AR[AvailabilityRepository]
    CSV[CsvExportService]
    DB[(SQLite: availability tables)]

    UI --> LEG --> AS --> AR --> DB
    LEG --> CSV
```

Current report grid uses availability data. Some named cards are route options over the same availability dataset; report-specific calculations need further extraction.

