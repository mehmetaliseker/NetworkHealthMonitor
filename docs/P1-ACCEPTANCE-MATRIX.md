# P1 Acceptance Matrix

Generated: 2026-07-31

Acceptance data prefix: `NHR-ACCEPTANCE-`

Runtime artifact: `artifacts/p1-runtime-20260731-acceptance/p1-runtime-report.json`

Pre-run database backup: `C:\ProgramData\NetworkHealthMonitor\backups\acceptance-before-p1-20260731-202015.db`

## Summary

| Flow | Test scenario | Test data | UI verified | Service verified | SQLite verified | Worker verified | Runtime result | Automated test | Status | Evidence |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Single ping | Active device ping writes log and updates device status | `NHR-ACCEPTANCE-UI-ONLINE`, `127.0.0.1` | PASS, real row action invoked | PASS | PASS | N/A for manual UI ping | Device became `Online`; one `PingLogs` row written | `Single_ping_writes_log_updates_device_and_rejects_inactive_device` | PASS | UIA row action; `acceptancePingLogCount=1`, `lastStatus=Online` |
| Bulk ping | Dashboard `Tüm Cihazları Kontrol Et` starts, shows progress/cancel, cancellation reaches command, completed run writes one row per active device | `NHR-ACCEPTANCE-BULK-*`, `127.0.0.1`, deterministic offline/timeout hosts | PASS, real WPF UI button and cancel button invoked | PASS | PASS | N/A for manual UI bulk ping | Cancel run wrote 0 logs; completed run wrote 3 logs, summary `Başarılı: 1, başarısız: 2` | `Bulk_ping_deduplicates_targets_reports_progress_and_skips_disabled_devices` | PASS | UIA Dashboard run; `acceptancePingLogCount=3`, duplicate count 0, cleanup count 0 |
| Worker device query | Service-created active devices visible to Worker candidate query | ProgramData SQLite acceptance devices | PARTIAL, UI left open for review | PASS | PASS | PASS | Worker queried and pinged two active acceptance devices | Runtime harness | PASS | `workerCandidatesAfterCreate=2`, `logsAfterInitialWorkerRun=2` |
| Scheduler | Due active group plan executes and updates ping logs | `NHR-ACCEPTANCE-P1-WORKER-GROUP` | NOT TESTED form creation through UI | PASS | PASS | PASS | Worker ran due plan and wrote scheduled logs | `Scheduler_runs_due_plan_once_continues_after_restart_and_ignores_inactive_plan` | PASS | Runtime log: plan checked 2 devices |
| Retry | First failure enters watch state before incident opens | `.invalid` offline hostname | NOT TESTED UI badge transition | PASS | PASS | PASS | Offline device moved to `UnderWatch`, later opened incident | `Retry_failure_opens_single_incident_queues_notifications_and_success_closes_it` | PASS | Runtime `offlineStatusAfterInitialRun=UnderWatch` |
| Incident open | Confirmed failure opens single incident | `NHR-ACCEPTANCE-OFFLINE` | NOT TESTED incidents page during run | PASS | PASS | PASS | One open incident created, duplicate count 0 | Automated + runtime harness | PASS | Runtime `openIncidentCount=1`, `duplicateOpenIncidentCount=0` |
| Incident close | Recovery ping closes open incident | Offline device changed to `localhost` | NOT TESTED incidents page during run | PASS | PASS | PASS | Open incident closed and recovery notifications queued/sent | Automated + runtime harness | PASS | Runtime `closedIncidentCount=1` |
| ntfy notification | Local fake ntfy HTTP endpoint receives outbox delivery | `http://127.0.0.1:<port>`, `nhm-p1-local` | NOT TESTED notification page | PASS | PASS | PASS | Local fake endpoint received initial and recovery posts | Runtime harness | PARTIAL | Runtime `fakeNtfyReceivedAfterRecovery=2`; real configured topic not used |
| SMTP notification | Local fake SMTP endpoint receives email delivery | `127.0.0.1:<port>`, `ops@example.invalid` | NOT TESTED notification page | PASS | PASS | PASS | Local fake SMTP received initial and recovery messages | Runtime harness | PARTIAL | Runtime `fakeSmtpReceivedAfterRecovery=2`; real SMTP config absent |
| Notification outbox | Incident creates outbox, dispatcher sends, duplicate keys prevented | Acceptance incident | NOT TESTED notification history page | PASS | PASS | PASS | Outbox count 2 after incident, 4 sent after recovery | `Notification_dispatcher_retries_transient_failure_and_continues_after_restart` | PASS | Runtime `sentOutboxAfterRecovery=4`, `duplicateOutboxIdempotencyCount=0` |
| Outbox retry | Transient channel failure returns to Pending, restart-like new dispatcher sends later | Temp test DB, fake channel | NOT TESTED | PASS | PASS | PARTIAL | Code-level dispatcher restart verified; real service restart blocked | Automated test | PARTIAL | `Notification_dispatcher_retries_transient_failure_and_continues_after_restart` |
| Worker restart durability | Stop/start service with pending outbox | Real Windows Service | NOT TESTED through UI | BLOCKED | NOT TESTED | BLOCKED | `sc.exe stop` denied by SCM permissions | Runtime harness | BLOCKED | Runtime `workerRestartStatus=BLOCKED`, service remained Running/Manual |
| UI/Tray/Worker sync | Surfaces report same service state | Real Worker service | PARTIAL | PASS | N/A | PASS | Worker health-check OK; UI and Tray processes are running | Existing UI tests + runtime service checks | PARTIAL | `Status=Running`, `StartMode=Manual`, heartbeat age under 10s |

## Runtime Evidence

Latest successful partial runtime run:

```text
createdOnlineDeviceId=7
createdOfflineDeviceId=8
createdPlanId=4
workerCandidatesAfterCreate=2
logsAfterInitialWorkerRun=2
onlineStatusAfterInitialRun=Online
offlineStatusAfterInitialRun=UnderWatch
openIncidentCount=1
outboxAfterIncidentOpen=2
sentOutboxAfterIncidentOpen=2
fakeNtfyReceivedAfterIncidentOpen=1
fakeSmtpReceivedAfterIncidentOpen=1
closedIncidentCount=1
sentOutboxAfterRecovery=4
fakeNtfyReceivedAfterRecovery=2
fakeSmtpReceivedAfterRecovery=2
workerRestartStatus=BLOCKED
startupTypeAfterRestart=Manual
duplicateOpenIncidentCount=0
duplicateOutboxIdempotencyCount=0
status=PARTIAL
```

Direct UI single-ping proof after fixing the row command parameter:

```text
device=NHR-ACCEPTANCE-UI-ONLINE
rowAction=Cihazi simdi kontrol et
lastStatus=Online
lastLatencyMs=0
pingCount=1
lastPingAt=2026-07-31T21:20:28.2135307+03:00
```

Direct UI bulk-ping proof:

```text
devices=NHR-ACCEPTANCE-BULK-acceptance-online,NHR-ACCEPTANCE-BULK-acceptance-offline,NHR-ACCEPTANCE-BULK-acceptance-timeout
firstRun=cancelled through Toplu cihaz kontrolünü iptal et
cancelResult=Ping işlemi iptal edildi; acceptancePingLogCount=0
secondRun=completed through Tüm cihazları kontrol et
uiSummary=Ping tamamlandı. Başarılı: 1, başarısız: 2, atlanan: 0.
sqlite=acceptancePingLogCount=3
liveStatus=acceptance result visible
cleanup=acceptanceDeviceCount=0, acceptancePingLogCount=0
```

## Cleanup Evidence

After runtime cleanup and settings restore:

```text
Table.Devices.Count=0
Table.SchedulePlans.Count=0
Table.PingLogs.Count=0
Table.DeviceIncidents.Count=0
Table.NotificationOutbox.Count=0
OpenIncidentDuplicateCount=0
OutboxIdempotencyDuplicateCount=0
Settings AutoCheckEnabled=false
Service Status=Running
Service StartMode=Manual
START_TYPE=3 DEMAND_START
Console Worker process count=0
```

## Blocked Items

| Flow | Blocker | Impact | Required evidence |
| --- | --- | --- | --- |
| Real Worker service restart | Current shell cannot stop service: `OpenService FAILED 5: Access denied` | Pending outbox after real SCM restart could not be proven in this run | Run harness from elevated shell or approve UAC through the UI service command |
| Real ntfy topic delivery | Existing topic `network-health-monitor123` was not confirmed as a dedicated test topic | Local fake ntfy path is verified, production topic was not touched | User-provided test topic/subscriber confirmation |
| Real SMTP delivery | SMTP is disabled and no host/recipient credentials are configured | Local fake SMTP path is verified, real email delivery was not attempted | User-provided test SMTP settings or approved test recipient |
