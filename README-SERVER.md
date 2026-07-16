# NetworkHealthMonitor Server Kurulumu

## Sistem gereksinimleri
- Windows Server 2019/2022/2025 veya Windows 10/11 x64.
- Yonetici PowerShell oturumu.
- ICMP Echo isteklerine izin veren firewall politikasi.
- Release paketindeki self-contained UI ve Worker; ayrica .NET runtime kurmak gerekmez.

## Ilk kurulum
Zip dosyasini kalici bir klasore acin:

```powershell
Expand-Archive .\NetworkHealthMonitor-Server-win-x64.zip C:\Apps\NetworkHealthMonitor -Force
cd C:\Apps\NetworkHealthMonitor
```

SHA-256 dosyasi kurulan bir program degildir; ZIP'in indirme/kopyalama sirasinda bozulmadigini dogrulamak icin kullanilan dosya butunlugu bilgisidir.

```powershell
Get-FileHash .\NetworkHealthMonitor-Server-win-x64.zip -Algorithm SHA256
Get-Content .\NetworkHealthMonitor-Server-win-x64.zip.sha256
```

Iki hash degeri ayni olmalidir.

Worker service kurulumu:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-service.ps1
```

Worker service yonetimi:

```powershell
.\scripts\start-service.ps1
.\scripts\service-status.ps1
.\scripts\stop-service.ps1
.\scripts\restart-service.ps1
.\scripts\uninstall-service.ps1
```

UI acma:

```powershell
.\ui\NetworkHealthMonitor.exe
```

UI kapali olsa bile Worker service kurulu ve Running durumundaysa otomatik ping devam eder. Worker calismiyorsa UI acilir ve manuel ping/cihaz/plan/ayar ekranlari kullanilabilir, ancak otomatik plan pingleri ve outbox dispatch arka planda ilerlemez.

## ProgramData dizini
UI ve Worker ayni veriyi kullanir:

```text
C:\ProgramData\NetworkHealthMonitor\data\network_health_monitor.db
C:\ProgramData\NetworkHealthMonitor\config\settings.json
C:\ProgramData\NetworkHealthMonitor\logs
C:\ProgramData\NetworkHealthMonitor\backups
```

Eski `%LocalAppData%\NetworkHealthMonitor` ve eski ProgramData kok veritabani ilk acilista kopyalanir, silinmez.

## Ilk cihaz ve plan
1. UI > Cihazlar > Yeni Cihaz.
2. IP, ad, tip ve grup girin.
3. Otomatik Kontrol Planlari ekraninda tum cihazlar, tip, grup veya tek cihaz hedefli plan olusturun.
4. Worker durumunu UI Ayarlar > Worker Sagligi bolumunden veya komutla dogrulayin:

```powershell
.\scripts\service-status.ps1
.\scripts\health-check.ps1
```

## ntfy bildirimleri
Telefonda ntfy uygulamasinda guvenli ve tahmin edilmesi zor bir topic'e abone olun. Gelistirme icin `https://ntfy.sh` kullanilabilir; uretimde kendi topic'inizi ve gerekirse access token kullanin.

UI > Ayarlar > Bildirimler:
- ntfy etkin
- Sunucu URL'si
- Konu
- Access token
- Kesinti ve duzelme bildirimleri
- Esikler, cooldown ve retry ayarlari

Test bildirimi:

```powershell
# UI'daki "Test bildirimi gonder" butonunu kullanin.
```

Self-hosted ntfy icin BaseUrl ornegi:

```text
https://ntfy.example.com
```

## Cihaz silme ve geri yukleme
Silme soft-delete yapar: `IsDeleted=1`, `IsEnabled=0`, `IsActive=0`, `AutoCheckEnabled=0` olur ve `DeletedAtUtc` yazilir. Cihaz otomatik kontrollerden cikar; gecmis ping loglari, outage ve incident kayitlari korunur.

UI destekleri:
- Cihazlar ekraninda tekli silme, coklu secimle "Secilenleri Sil" ve sag tik menusu.
- Cihaz Gruplari ekraninda "Bu gruptaki tum cihazlari sil"; istege bagli olarak bos grup da kaldirilabilir.
- Silinen cihazlar filtresiyle tekli veya toplu geri yukleme.

Worker silinmis/pasif cihazlari scheduler hedeflerine dahil etmez.

## CSV import modlari
UI > Cihazlar ekraninda CSV icin once mod ve kapsam secilir, sonra "CSV Onizle" ile dry-run/fark analizi alinir. "Ice Aktar" butonu onizleme alinmadan uygulanmaz.

Modlar:
- Sadece yeni cihazlari ekle: mevcut cihazlara dokunmaz, yeni IP'leri ekler.
- Ekle veya guncelle: CSV'deki IP mevcutsa desteklenen alanlari gunceller, yeni IP'leri ekler.
- CSV ile tamamen esitle: CSV'de olmayan aktif cihazlari soft-delete yapar; gecmis ping ve kesinti kayitlari korunur.

Esitleme kapsami:
- Tum aktif cihazlari CSV ile esitle.
- Yalnizca secili grubu CSV ile esitle; diger gruplara dokunulmaz.

Guvenlik kurallari:
- Bos veya yalniz header iceren CSV uygulanmaz.
- Gecerli satir sayisi 0 ise import durur.
- Duplicate IP bulunan CSV uygulanmaz.
- Import oncesi otomatik veritabani backup alinir.
- Degisiklikler transaction icinde uygulanir; hata halinde rollback yapilir.
- Import sonucu `CsvImportAudits` tablosuna yazilir.

Desteklenen basliklar eski export formatini korur ve ek alanlari kabul eder: `Name`, `IpAddress`, `DeviceType`, `GroupName`, `Location`, `Description`, `IsCritical`, `IsEnabled`, `AutoCheckEnabled`, `PingTimeoutMs`, `CheckIntervalSeconds`, `RetryIntervalSeconds`, `RetryLimit`, `FailureThreshold`. Turkce baslik alias'lari da desteklenir.

## Bildirim Kuyrugu
UI > Bildirim Kuyrugu ekraninda outbox kayitlari durum, bildirim turu, cihaz ve tarih araligina gore filtrelenebilir.

Islemler:
- Failed kaydi tekrar dene: kayit `Pending` yapilir, lock alanlari temizlenir, `NextAttemptAtUtc` simdiye cekilir.
- Secili Failed kayitlari tekrar dene.
- Pending kaydi iptal et.
- Ayrinti goruntule.

Sent kayitlar yeniden gonderilmez. 401/403 hatali failed kayitlarda once ntfy BaseUrl/topic/access token ayarlarini kontrol edin.

## UI otomatik baslangic
Worker Windows Service olarak automatic/delayed automatic calisir ve kullanici oturumu acilmadan da ping atar. UI otomatik baslangici zorunlu degildir.

UI > Ayarlar:

```text
Windows'a giris yaptigimda yonetim ekranini ac
```

Bu secenek yalnizca WPF yonetim ekranini etkiler. Etkinlestirildiginde kullanici Startup klasorune `NetworkHealthMonitor.lnk` olusturulur:

```text
%AppData%\Microsoft\Windows\Start Menu\Programs\Startup
```

Scriptler:

```powershell
.\scripts\install-ui-autostart.ps1 -UiPath .\ui\NetworkHealthMonitor.exe
.\scripts\uninstall-ui-autostart.ps1
```

## Availability, Coverage ve SLA raporlari
Uygulamadaki raporlar gercek cihaz/OS uptime degeri degil, ping tabanli **ag erisilebilirligi** raporudur. Ping yaniti alinmamasi cihaz kapali anlamina tek basina gelmez; ICMP/firewall/ag politikasi da etkili olabilir.

Yeni source-of-truth tablolar:
- `DeviceAvailabilityPeriods`: Up, Down, Unknown, Maintenance ve Paused periodlari.
- `DeviceAvailabilityDaily`: gunluk aggregate kayitlari.
- `MonitoringCalendars`, `MonitoringCalendarRules`, `DeviceMonitoringCalendarAssignments`: izleme beklenen zamanlar.
- `MaintenanceWindows`, `MaintenanceWindowTargets`: planli bakim araliklari.
- `AvailabilityRecalculationAudits`: rebuild audit kayitlari.

Temel formuller:

```text
KnownSeconds = UpSeconds + DownSeconds
AvailabilityPercent = UpSeconds / KnownSeconds * 100
StrictAvailabilityPercent = UpSeconds / (UpSeconds + DownSeconds + UnknownSeconds) * 100
CoveragePercent = KnownSeconds / ExpectedMonitoringSeconds * 100
Genel availability = Sum(UpSeconds) / Sum(UpSeconds + DownSeconds) * 100
```

Down dogrulama davranisi:
- `FirstFailureAtUtc`: ilk basarisiz ping.
- `ConfirmedDownAtUtc`: esik dolup Down dogrulanan ping.
- `DetectionDelaySeconds = ConfirmedDownAtUtc - FirstFailureAtUtc`.
- Varsayilan downtime baslangici `FirstFailedCheck` politikasidir; ayarlardan `ConfirmedDownTime` secilebilir.

Worker heartbeat veya beklenen kontrol araligi asilirsa bosluk Up sayilmaz; period `Unknown` olur. Worker tekrar basladiginda gecmis bosluk otomatik Up'a doldurulmaz. Bakim pencereleri `Maintenance` olarak sayilir ve plansiz downtime'a eklenmez.

UI > Ag Erisilebilirligi:
- Mevcut durum, durum baslangici, kesintisiz erisilebilir sure, Unknown, Maintenance ve Coverage kolonlari.
- Availability summary CSV export.
- Incident CSV export.
- Son 30 gun availability verilerini yeniden hesaplama.

UI > Dashboard:
- Toplam aktif cihaz, Up, Down, Unknown, Maintenance, acik incident, 24 saat/7 gun/30 gun genel availability, coverage, SLA ihlali ve failed notification kartlari.
- Genel availability basit cihaz ortalamasi degil, `Sum(UpSeconds) / Sum(UpSeconds + DownSeconds)` formuluyle sure agirlikli hesaplanir.
- Son 30 gun trendi, grup bazli availability, en uzun kesintiler, en cok incident yasayan cihazlar, Unknown suresi yuksek cihazlar ve coverage dusuk cihazlar DataGrid olarak izlenir.

UI > Cihazlar:
- Secili cihaz detayinda mevcut availability durumu, durum baslangici, kesintisiz erisilebilir sure, devam eden kesinti, ilk hata, Down dogrulama zamani, son kontrol, 24s/7g/30g availability, coverage, MTTR, MTBF ve SLA durumu gosterilir.
- Period timeline sekmesi Up, Down, Unknown ve Maintenance periodlarini tarih filtresiyle listeler.

UI > Bakim:
- Maintenance window olusturma, duzenleme, iptal ve bitirme desteklenir.
- Hedef tipi cihaz, grup veya tum cihazlar olabilir. Bakim sureleri plansiz downtime'a eklenmez.
- Bildirim bastirma ve ping islemlerine devam etme bayraklari kaydedilir.

UI > Izleme Takvimleri:
- 7/24 veya gun/saat aralikli monitoring calendar olusturulabilir.
- Timezone, cihaz/grup/tum cihaz atamasi ve varsayilan takvim secimi desteklenir.
- Takvim disindaki sureler availability hesaplarinda beklenen izleme suresi olarak sayilmaz.

SLA:
- Cihaz ve grup icin %99, %99.5, %99.9, %99.95, %99.99 hedefleri secilebilir.
- Cihaz uzerindeki SLA hedefi grup hedefini override eder; cihaz bos birakilirsa grup hedefi rapora devredilir.

UI > Sistem Durumu:
- Service kurulum/running/startup/recovery, heartbeat freshness, SQLite erisimi, outbox sayilari, disk bos alani, backup zamani, UI/Worker surumleri ve uzun sureli calisma diagnostics izlenir.
- Service Running olsa bile heartbeat toleransi asilmissa sistem saglikli kabul edilmez.

UI > Ayarlar:
- Heartbeat toleransi.
- Beklenen kontrol boslugu katsayisi.
- Downtime baslangic politikasi.
- Ping log, availability period, incident ve daily aggregate retention ayarlari.

## Backup

```powershell
.\scripts\backup-data.ps1
```

## Restore

```powershell
.\scripts\restore-data.ps1 -BackupPath "C:\ProgramData\NetworkHealthMonitor\backups\20260715-120000"
```

Restore mevcut veriyi once ayrica yedekler, servisi durdurur, veriyi geri yukler ve health-check calistirir.

## Guncelleme

```powershell
.\scripts\upgrade-service.ps1 -NewWorkerPath "C:\Apps\NetworkHealthMonitor\worker"
```

`NewWorkerPath` worker publish klasoru veya tek worker exe olabilir. Klasor verilirse dependency DLL'leri de kopyalanir.

Onerilen guncelleme sirasi:
1. Eski kurulum klasorunu silmeden once `.\scripts\backup-data.ps1` ile backup alin.
2. Yeni ZIP'i yeni bir kalici klasore acin.
3. Yonetici PowerShell ile yeni klasorde `.\scripts\upgrade-service.ps1 -NewWorkerPath ".\worker"` calistirin.
4. `.\scripts\service-status.ps1` ve `.\scripts\health-check.ps1` ile Worker sagligini kontrol edin.
5. UI'yi `.\ui\NetworkHealthMonitor.exe` ile acin.

Eski surumden ilk geciste `%LocalAppData%\NetworkHealthMonitor\network_health_monitor.db` veya `C:\ProgramData\NetworkHealthMonitor\network_health_monitor.db` bulunursa aktif DB yokken `C:\ProgramData\NetworkHealthMonitor\data\network_health_monitor.db` konumuna kopyalanir, kaynak dosya silinmez ve backup olusturulur. Aktif DB zaten varsa eski kaynak tekrar kopyalanip guncel semayi ezmez.

## Production readiness testi

Publish paketinde son kurulum oncesi otomatik kontrol:

```powershell
.\scripts\production-readiness-test.ps1
```

Script her kontrol icin `PASS` veya `FAIL` yazar. Zorunlu kontrollerden biri basarisizsa non-zero exit code doner. Kontroller worker/UI exe varligi, service kurulum ve Running durumu, Automatic startup, recovery actions, ProgramData yazilabilirligi, Worker health-check ile SQLite/outbox/heartbeat, backup, disk alani ve UI/Worker surum eslesmesini kapsar.

## Servisi kaldirma

```powershell
.\scripts\uninstall-service.ps1
```

Veriyi de silmek icin acik parametre gerekir:

```powershell
.\scripts\uninstall-service.ps1 -PurgeData
```

## Loglar

```text
C:\ProgramData\NetworkHealthMonitor\logs
```

UI Ayarlar ekranindan "Log klasorunu ac" komutu da kullanilabilir.

## Yaygin hatalar
- Ping gelmiyor: Windows Firewall ICMP Echo kuralini kontrol edin.
- Worker calisiyor ama ping atmiyor: plan aktif mi, `NextRunAtUtc` zamani gelmis mi, cihaz silinmis/pasif mi kontrol edin.
- SQLite locked: UI ve Worker kisa transaction/WAL kullanir; uzun sureli antivirus veya backup kilitlerini kontrol edin.
- Bildirim gelmiyor: topic, BaseUrl, token, outbox pending/failed sayilari ve `health-check.ps1` ciktisini kontrol edin.
- Script health-check: PowerShell artik `Microsoft.Data.Sqlite.dll` icin `Add-Type` kullanmaz; publish Worker exe `--health-check` komutunu calistirir ve hata kodunu tasir.
- `no such column` veya `no such table`: UI ve Worker'i kapatip `.\scripts\health-check.ps1` calistirin. Hata devam ederse `C:\ProgramData\NetworkHealthMonitor\logs` altindaki son logda DB yolu, migration kimligi ve eksik tablo/sutun bilgisi yer alir.
