# NetworkHealthMonitor Mimari Notları

NetworkHealthMonitor, şirket içindeki IP tabanlı cihazların ICMP ping ile izlenmesi, sonuçların SQLite üzerinde saklanması ve uptime raporlarının üretilmesi için geliştirilmiş WPF masaüstü uygulamasıdır. Mevcut sürümde hedef, 300+ cihazı tek kullanıcı arayüzünden kontrollü, sade ve raporlanabilir şekilde takip etmektir.

## Katmanlar

### Models

Domain ve UI binding modellerini içerir. `Device`, `DeviceGroup`, `SchedulePlan`, `PingLog`, `Outage`, `AppSettings`, `DeviceCheckPolicy`, `DeviceTypePolicy` ve rapor modelleri bu katmandadır. Cihaz durum metinleri kullanıcıyı yanıltmayacak şekilde model/enum extension seviyesinde tutulur.

### ViewModels

WPF ekran state'i ve command bağlama katmanıdır. `MainViewModel` partial dosyalara bölünmüştür:

- `MainViewModel.cs`: alanlar, constructor, public binding property'leri ve command property'leri.
- `MainViewModel.DeviceCommands.cs`: cihaz, grup ve manuel ping komutları.
- `MainViewModel.ScheduleCommands.cs`: plan ve scheduler komutları.
- `MainViewModel.SettingsCommands.cs`: ayar, bakım ve log temizlik komutları.
- `MainViewModel.ImportExportCommands.cs`: CSV import/export komutları.
- `MainViewModel.BulkOperations.cs`: seçili cihazlara toplu işlem komutları.
- `MainViewModel.Dashboard.cs`: dashboard özetleri.
- `MainViewModel.Refresh.cs`: veri yükleme, filtreleme ve command state yenileme.

ViewModel SQL yazmaz, CSV string üretmez, uptime hesaplamaz ve retry kararını kendi içinde vermez. Bu işler servis ve repository katmanlarına bırakılır.

### Services

İş mantığı ve koordinasyon katmanıdır.

- `PingService`: ICMP ping işlemini yapar.
- `PingExecutionService`: ping görevlerini koordine eder, duplicate guard ve paralellik sınırını uygular.
- `SchedulerService`: zamanı gelen otomatik kontrolleri çalıştırır.
- `SchedulePlanTargetResolver`: plan hedeflerini cihaz listesine çözer.
- `DeviceCheckPolicyService`: cihaz/grup/tip/global policy önceliğini çözer.
- `DeviceHealthEvaluator`: ping sonucu ve policy bilgisine göre sağlık durumunu değerlendirir.
- `AvailabilityService`: uptime/erişilebilirlik hesaplamasını repository ile koordine eder.
- `CsvExportService` ve `DeviceImportExportService`: CSV rapor, cihaz import ve export akışlarını yürütür.
- `DataMaintenanceService`: SQLite yedekleme, restore, optimize ve ayar dosyası taşıma işlemlerini yürütür.
- `AppSettingsService`: `settings.json` okuma/yazma ve ayar normalizasyonundan sorumludur.

### Data / Repositories

SQLite erişimi bu katmandadır. Repository sınıfları CRUD, aggregate rapor, import uygulama, log temizleme ve migration sonrası sorguları yürütür. ViewModel doğrudan SQL kullanmaz.

### Export / Import

CSV dosyaları UTF-8 BOM ile yazılır. Varsayılan ayırıcı Türkiye Excel uyumu için `;` karakteridir. Import sırasında geçersiz IP, eksik zorunlu alan, bilinmeyen cihaz tipi ve duplicate IP durumları kullanıcıya raporlanır.

### Maintenance

Bakım akışı SQLite dosyasının yedeklenmesi, restore edilmesi, eski ping loglarının retention değerine göre temizlenmesi ve `PRAGMA optimize` çalıştırılmasını kapsar. Cleanup scheduler tick'inde değil, düşük maliyetli noktalarda veya manuel komutla çalışır.

## Ping Akışı

1. Kullanıcı manuel ping başlatır veya scheduler zamanı gelen planı çalıştırır.
2. Hedef cihazlar repository/target resolver üzerinden belirlenir.
3. `DeviceCheckPolicyService` her cihaz için etkin policy değerlerini çözer.
4. `PingExecutionService` aynı cihaza duplicate ping atılmasını engeller ve `MaxParallelPings` sınırını uygular.
5. `PingService` ICMP ping sonucunu döndürür.
6. Sonuç `PingLogs` tablosuna yazılır, cihazın son durum alanları güncellenir.
7. Uptime ve dashboard verileri loglardan aggregate sorgularla yenilenir.

## Scheduler Akışı

Scheduler global poll aralığıyla zamanı gelen aktif planları kontrol eder. Pasif planlar, pasif cihazlar ve `AutoCheckEnabled=false` cihazlar otomatik kontrole alınmaz. Başarısız cihazlar global scheduler hızını değiştirmez; yalnız ilgili cihazın bir sonraki kontrol zamanı policy ile kısaltılır.

## Akıllı Retry Mantığı

Başarısız ping tek başına kesin arıza kabul edilmez. Ardışık başarısızlık sayısı artar ve policy izin veriyorsa yalnız o cihaz kısa retry aralığıyla tekrar kontrol edilir. Başarılı ping geldiğinde failure sayacı sıfırlanır ve cihaz normal kontrol aralığına döner.

Durum geçişleri kullanıcıya daha temkinli metinlerle gösterilir:

- Sağlıklı
- Uyarı
- Takipte
- Muhtemel erişilemiyor
- Ping yanıtlamıyor olabilir
- Kontrol edilmedi

## Policy Önceliği

Etkin policy tek bir servis tarafından şu sırayla çözülür:

1. Cihaz özel ayarı
2. Grup varsayılanı
3. Tip varsayılanı
4. Global ayar

Policy kapsamı:

- Otomatik kontrol açık/kapalı
- Normal kontrol aralığı
- Ping timeout
- Hızlı retry aralığı
- Retry limiti
- Başarısızlık eşiği

## Uptime Hesaplama

Uptime cihaz üzerinde sabit bir yüzde olarak tutulmaz. `PingLogs` kayıtlarından SQL aggregate sorguları ile hesaplanır.

Temel formül:

```text
uptime = başarılı ölçüm sayısı / toplam ölçüm sayısı * 100
```

Desteklenen dönemler:

- Son 24 saat
- Son 7 gün
- Son 30 gün
- Genel

Bu hesaplama ViewModel içinde yapılmaz; repository/service katmanında çalışır.

## SQLite Performans Stratejisi

SQLite şu aşamada tek masaüstü uygulaması ve 300+ cihaz hedefi için yeterlidir. Log büyümesi asıl risk olduğu için şu önlemler kullanılır:

- WAL modu
- `busy_timeout`
- `PingLogs` için `DeviceId`, `CheckedAt`, `Status` ve birleşik indexler
- Plan ve cihaz otomatik kontrol sorguları için indexler
- Retention ile eski log temizleme
- Uptime ve CSV export için aggregate SQL sorguları
- Manuel `PRAGMA optimize`

Çok yıllı log saklama, merkezi çok kullanıcılı erişim, yoğun alarm iş akışı veya sunucu tarafı raporlama gerektiğinde PostgreSQL ya da SQL Server değerlendirilmelidir.

## HTTP/TCP/SNMP Kararı

Mevcut sürüm ICMP ping odaklıdır. Şirketteki cihazlar ping alabildiği için HTTP, TCP port ve SNMP kontrolleri şu an aktif özellik olarak eklenmemiştir. Mimari ileride `TcpPortCheckService`, `HttpCheckService` ve `SnmpCheckService` gibi servislerin `PingService` benzeri ayrı implementation olarak eklenmesine uygundur.

Bu genişleme yapılırken ViewModel'e kontrol protokolü mantığı gömülmemeli; check type seçimi ayrı servis/policy katmanında tutulmalıdır.

## Alarm Sistemi Kararı

Alarm sistemi bu sürümde yoktur. Mevcut outage altyapısı erişilememe dönemlerini raporlamak için kullanılabilir, ancak bildirim/eskalasyon işi ping akışının içine gömülmemelidir. İleride alarm sistemi eklenecekse ping sonuçlarını dinleyen ayrı bir `AlarmEvaluationService` ve ayrı notification adapter'ları kullanılmalıdır.

## Windows Service Geçişi

Bu sürüm WPF masaüstü uygulamasıdır. Windows Service'e geçiş şu an yapılmamıştır. İleride scheduler ve ping execution katmanları arka plan servisine taşınabilir; WPF tarafı ise yalnız yönetim ve raporlama arayüzü olarak kalabilir. Bu ayrım yapılırken repository, settings ve policy servisleri ortak kullanılabilecek şekilde korunmalıdır.
