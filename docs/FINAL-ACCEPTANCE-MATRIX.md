# Final Acceptance Matrix

Generated: 2026-07-31

Host context: non-elevated PowerShell, real `NetworkHealthMonitorWorker` service running as `Manual / DEMAND_START`, ProgramData SQLite available.

Status values: `PASS`, `PARTIAL`, `BLOCKED`, `FAIL`, `NOT TESTED`.

| Scope | Check | Status | Evidence |
| --- | --- | --- | --- |
| P1 | Single device ping from real WPF UI writes SQLite ping log and refreshes device state | PASS | `docs/P1-ACCEPTANCE-MATRIX.md`; UIA row action; `PingLogs=1`, `LastStatus=Online` |
| P1 | Bulk ping from real WPF UI supports progress, cancel, completion summary and SQLite persistence | PASS | Dashboard UIA run; cancel wrote `0` logs; completed run wrote `3` logs; summary `Başarılı: 1, başarısız: 2` |
| P1 | Worker device query reads active UI-created devices from same SQLite DB | PASS | P1 runtime harness; `workerCandidatesAfterCreate=2`, scheduled logs written |
| P1 | Scheduler executes active due plan and persists next run | PASS | P1 runtime harness plus `Scheduler_runs_due_plan_once_continues_after_restart_and_ignores_inactive_plan` |
| P1 | Retry opens and closes incident through deterministic failure/recovery | PASS | `Retry_failure_opens_single_incident_queues_notifications_and_success_closes_it`; runtime open/close counts |
| P1 | Notification outbox creates, deduplicates, retries and sends through worker dispatcher | PASS | `Notification_dispatcher_retries_transient_failure_and_continues_after_restart`; runtime `sentOutboxAfterRecovery=4` |
| P1 | ntfy real endpoint | PARTIAL | Local fake HTTP endpoint received expected messages; real test topic not provided/approved |
| P1 | SMTP real endpoint | PARTIAL | Local fake SMTP server received expected messages; real SMTP config/test recipient not provided |
| P1 | Worker restart through SCM with pending scheduler/outbox | BLOCKED | Current shell is not administrator; SCM stop/start needs elevation |
| P1 | UI, Tray and Worker state sync | PARTIAL | Worker SCM/heartbeat PASS; UI and Tray live from previous run, but elevated stop/start sync not completed |
| P2 | Real runtime backup while Worker is running | PASS | `artifacts/p2-ops-20260731/p2-ops-report.json`; backup `C:\ProgramData\NetworkHealthMonitor\backups\NetworkHealthMonitor-20260731-215924.db`, schema verify PASS |
| P2 | Restore from valid backup | PASS | Automated isolated DB test `Backup_restore_verify_and_corrupt_restore_rollback_preserve_current_database`; not run against primary DB by design |
| P2 | Corrupt restore rejected and current DB preserved | PASS | Same automated isolated DB test; `DataMaintenanceService` verifies source before replacing current DB |
| P2 | CSV import/export data path | PASS | `--p2-ops` real ProgramData run imported and cleaned `NHR-ACCEPTANCE-P2-CSV`; export escaped spreadsheet formula |
| P2 | CSV OpenFileDialog/SaveFileDialog manual UI | NOT TESTED | Service path verified; OS file dialog manual proof not captured in this run |
| P2 | Uptime report CSV from real SQLite ping data | PASS | `--p2-ops` wrote two runtime ping logs, exported `p2-uptime-report.csv`, then cleaned data |
| P2 | SLA, MTTR/MTBF, ping performance report UI | PARTIAL | Real repository/export infrastructure exists; this run verified uptime CSV only |
| P2 | Settings persistence | PASS | `--p2-ops` changed/reloaded settings and restored original settings file; automated settings test also PASS |
| P2 | Worker auto-start remains off | PASS | `sc.exe qc NetworkHealthMonitorWorker`: `START_TYPE : 3 DEMAND_START`; `StartMode=Manual` |
| Installation | Self-contained UI publish for `win-x64` | PASS | `artifacts/production/UI/NetworkHealthMonitor.exe` exists, version `1.1.0` |
| Installation | Self-contained Tray publish for `win-x64` | PASS | `artifacts/production/Tray/NetworkHealthMonitor.Tray.exe` exists, version `1.1.0` |
| Installation | Self-contained Worker publish for `win-x64` | PASS | `artifacts/production/Worker/NetworkHealthMonitor.Worker.exe` exists, version `1.1.0` |
| Installation | Program Files UI/Tray install | BLOCKED | Non-elevated shell cannot write `C:\Program Files\NetworkHealthMonitor`; existing Program Files has Worker only |
| Installation | Worker service `binPath` points to production Worker EXE | PASS | SCM `BINARY_PATH_NAME = C:\Program Files\NetworkHealthMonitor\Worker\NetworkHealthMonitor.Worker.exe` |
| Installation | PowerShell installer preserves ProgramData and installs Worker as Manual | PASS | `scripts/Install-NetworkHealthMonitor.ps1` updated; not executed in this shell due elevation requirement |
| Installation | Setup EXE | BLOCKED | WiX/Inno/MSIX tooling not available in this environment; PowerShell installer and portable ZIP produced |
| Upgrade | Existing ProgramData preserved by install scripts | PARTIAL | Script behavior reviewed; no elevated upgrade execution in this run |
| Rollback | Restore rollback for DB | PASS | Automated corrupt restore rollback test |
| Rollback | Binary rollback after failed upgrade | PARTIAL | Documented/script-supported path; no elevated old/new binary swap executed |
| Clean Windows | Install on clean Windows without .NET SDK | NOT TESTED | No Windows Sandbox/VM available from current session |
| UI | Production artifact UI launches from self-contained output | PASS | Process `32980` from `artifacts/production/UI`; UIA found Dashboard, 12 nav items and `Çalışıyor` Worker state |
| Tray | Production artifact Tray launches from self-contained output | PASS | Process `29184` from `artifacts/production/Tray`; Program Files Tray install remains blocked by non-admin shell |
| Worker Service | Running, single SCM worker, no console worker | PASS | Final process/service checks; console worker process count `0` after verify commands |
| SQLite | Integrity, FK and schema contract | PASS | Published Worker `--verify-database`: `IntegrityCheck=ok`, `ForeignKeyCheck=ok`, `SchemaContractOk=True` |
| Backup/restore | Primary DB not deleted or replaced during test | PASS | P2 runtime used backup only on primary; restore tests ran on isolated DB |
| ntfy | Real production/test topic | PARTIAL | Local fake endpoint only |
| SMTP | Real production/test mailbox | PARTIAL | Local fake server only |
| Security | Secret pattern scan | PASS | `production-readiness-test.ps1` secret scan PASS |
