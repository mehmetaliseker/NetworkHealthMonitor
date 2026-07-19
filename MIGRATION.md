# Database Migration

Varsayilan veritabani: `C:\ProgramData\NetworkHealthMonitor\data\network_health_monitor.db`.

Bu surumde eklenen migration:

- `2026071902-extended-scheduler`

Migration eski `SchedulePlans.IntervalMinutes` degerlerini yeni sabit aralik modeline tasir. Eski planlar davranis degistirmeden calismaya devam eder.

Migration oncesi otomatik backup `C:\ProgramData\NetworkHealthMonitor\backups` altinda olusturulur. UI ve Worker ayni anda migration calistirmamasi icin global mutex kullanilir.
