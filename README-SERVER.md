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

Kurulum scripti servisi `Automatic (Delayed Start)` olarak ayarlar. Recovery politikasi ilk hata icin 1 dakika, ikinci hata icin 5 dakika, sonraki hatalar icin 15 dakika sonra yeniden baslatmadir. Worker UI acilmadan ve kullanici oturumu acilmadan calismaya devam eder.

UI farkli bir Windows kullanicisiyle calisacaksa bu kullaniciyi acikca verin. Script `C:\ProgramData\NetworkHealthMonitor` ACL'ini SYSTEM, Administrators ve verilen UI kullanicisiyle sinirlar:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-service.ps1 -UiUser "DOMAIN\kullanici"
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
SMTP parolasi ve ntfy token `settings.json` icinde DPAPI LocalMachine ile korunmus `dpapi:` degeri olarak saklanir. Dosya ACL'i service kurulum scriptiyle sinirlandirilmalidir; kurulum farkli admin hesabi ile yapildiysa UI kullanicisini `-UiUser` ile belirtin.

## Ilk cihaz ve plan
1. UI > Cihazlar > Yeni Cihaz.
2. IP, ad, tip ve grup girin.
3. Otomatik Kontrol Planlari ekraninda tum cihazlar, tip, grup veya tek cihaz hedefli plan olusturun.
4. Worker durumunu UI Ayarlar > Worker Sagligi bolumunden veya komutla dogrulayin:

```powershell
.\scripts\service-status.ps1
.\scripts\health-check.ps1
```

Ornek 1.1.0 plani:
- Normal kontrol: gunde 4 kez, esit dagitilmis.
- Hizli dogrulama: ilk hata sonrasi 3 yeniden deneme, 60 saniye aralik.
- Erisilemeyen cihaz kontrolu: 20 dakikada bir.
- Cihaz tekrar erisilebilir olunca normal plana doner.

Sabit aralik modu 1 dakika ile 365 gun arasinda deger kabul eder. Gunluk saat listeleri en fazla 48 saat destekler. Haftalik planlarda en az bir gun ve bir saat secilmelidir.

## ntfy bildirimleri
Telefonda ntfy uygulamasinda guvenli ve tahmin edilmesi zor bir topic'e abone olun. Gelistirme icin `https://ntfy.sh` kullanilabilir; uretimde kendi topic'inizi ve gerekirse access token kullanin.

UI > Ayarlar > ntfy:
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

## SMTP ve e-posta bildirimleri
UI > Ayarlar > E-posta / SMTP:
- E-posta bildirimlerini etkinlestirin.
- SMTP sunucu, port ve guvenlik turunu secin: STARTTLS, SSL/TLS veya yalnizca bilincli olarak guvensiz baglanti.
- Kullanici adi, parola, gonderen adresi/adini girin.
- Timeout ve retry sayisini ayarlayin.
- Once "Baglantiyi test et", ardindan "Test e-postasi gonder" kullanin.

Kayitli parola UI'da maskeli gosterilir. Parola alanini bos birakarak kaydederseniz mevcut secret korunur. SMTP gecici olarak ulasilamazsa ayarlar kaybedilmez; outbox kayitlari retry veya DeadLetter durumuna gecer.

## E-posta alicilari
UI > Ayarlar > E-posta Alicilari:
- Ilk cevrimdisi bildirim alicilari ve uzun sure cevrimdisi kalma alicilari ayridir.
- Her adres tek tek dogrulanir.
- Ayni adres ayni listede iki kez kaydedilmez.
- Liste degisikliklerini Worker bir sonraki ayar okumasinda kullanir; servis yeniden baslatma zorunlu degildir.

Escalation suresi UI > Ayarlar > E-posta / SMTP veya Bildirim ayarlarindan toplam saat olarak girilir. Varsayilan 48 saattir; minimum 1 saat, maksimum 8760 saattir.

## E-posta sablonlari
UI > Ayarlar > Bildirim Sablonlari:
- Ilk cevrimdisi, escalation ve cihaz duzeldi sablonlari ayridir.
- Konu bos birakilamaz.
- Bilinen placeholder'lar ekranda listelenir ve onizleme gercekci ornek cihaz verisiyle uretilir.
- Bilinmeyen placeholder varsa kaydetmeden once uyari verilir.
- "Varsayilana dondur" secenegi profesyonel Turkce sablonlari geri yukler.

Desteklenen placeholder'lar: `{DeviceName}`, `{IpAddress}`, `{DeviceType}`, `{GroupName}`, `{Status}`, `{IncidentStartedAt}`, `{LastSuccessfulCheckAt}`, `{LastCheckAt}`, `{OfflineDuration}`, `{EscalationThreshold}`, `{ApplicationName}`.

## Susturma ve izlemeyi durdurma
UI > Cihazlar ekraninda tek cihaz veya toplu secim icin iki farkli mod vardir:
- Bildirimleri sustur: ping ve loglar devam eder; e-posta ve ntfy gonderilmez.
- Izlemeyi gecici durdur: otomatik ping, uptime basarisiz kaydi ve bildirimler secilen sure boyunca durur.

Sure secenekleri 30 dakika, 1 saat, 4 saat, 8 saat, 1 gun, 2 gun, 1 hafta, ozel tarih/saat veya suresizdir. Neden alaninda bakim, cihaz degisimi, hat problemi, gecici kullanim disi, planli calisma veya diger secilebilir.

Izleme durdurulan sure escalation hesabina dahil edilmez. Bildirimleri susturma sureleri de escalation zamanini ileri tasir; susturma biter bitmez gecmis sure nedeniyle duplicate escalation uretilmez.

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

## Final kabul dogrulamasi
Release paketi self-contained gelir; temiz Windows bilgisayarda `sqlite3`, .NET SDK, Visual Studio veya kaynak kod gerekmez.

Worker veritabani dogrulama komutlari:

```powershell
.\worker\NetworkHealthMonitor.Worker.exe --database-integrity-check
.\worker\NetworkHealthMonitor.Worker.exe --database-summary
.\worker\NetworkHealthMonitor.Worker.exe --migration-status
.\worker\NetworkHealthMonitor.Worker.exe --verify-database
```

JSON ve insan tarafindan okunabilir rapor uretmek icin:

```powershell
.\worker\NetworkHealthMonitor.Worker.exe --verify-database `
  --database-report-json .\release\verification\database-verify.json `
  --database-report-text .\release\verification\database-verify.txt
```

Final build scriptinin bekledigi kanit dosyalari ve ureten scriptler:

```text
release\verification\windows-service-acceptance.json
  scripts\windows-service-acceptance-test.ps1

release\verification\migration-acceptance.json
  scripts\migration-acceptance-test.ps1

release\verification\notification-acceptance.json
  scripts\notification-acceptance-test.ps1

release\verification\ui-acceptance.json
  scripts\ui-acceptance-test.ps1

release\verification\soak-test.json
  scripts\soak-test.ps1

release\verification\upgrade-rollback-acceptance.json
  scripts\upgrade-rollback-acceptance-test.ps1
```

Migration kabul testi ornegi:

```powershell
.\scripts\migration-acceptance-test.ps1 `
  -OldDatabasePath C:\Temp\old\network_health_monitor.db `
  -OldSettingsPath C:\Temp\old\settings.json `
  -WorkerPath .\worker\NetworkHealthMonitor.Worker.exe
```

Manuel kanit gerektiren scriptler varsayilan olarak `NOT TESTED` uretir. Gercek reboot, gercek SMTP/ntfy alimi, UI ekran olceklendirme testi, 8 saat soak ve upgrade/rollback kanitlari olmadan final release paketi uretilmez.
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
- SMTP kimlik dogrulama hatasi: kullanici adi, uygulama parolasi, TLS turu ve gonderen adresinin sunucu tarafindan yetkili oldugunu kontrol edin.
- SMTP timeout: sunucu/port/firewall, STARTTLS gereksinimi ve timeout suresini kontrol edin.
- E-posta gonderilmiyor: E-posta etkin mi, alici listesi bos mu, sender adresi gecerli mi ve Bildirimler ekraninda Failed/DeadLetter kaydi var mi kontrol edin.
- ntfy gonderilmiyor: topic, BaseUrl, token, outbox pending/failed sayilari ve `health-check.ps1` ciktisini kontrol edin.
- Script health-check: PowerShell artik `Microsoft.Data.Sqlite.dll` icin `Add-Type` kullanmaz; publish Worker exe `--health-check` komutunu calistirir ve hata kodunu tasir.
- `no such column` veya `no such table`: UI ve Worker'i kapatip `.\scripts\health-check.ps1` calistirin. Hata devam ederse `C:\ProgramData\NetworkHealthMonitor\logs` altindaki son logda DB yolu, migration kimligi ve eksik tablo/sutun bilgisi yer alir.
- Service kurulamazsa: PowerShell'i yonetici olarak actiginizi ve ZIP'i kalici/yazilabilir bir klasore cikardiginizi dogrulayin.
