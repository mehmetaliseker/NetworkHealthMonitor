# Network Health Monitor

Türkçe arayüz başlığı: **Ağ Cihazları Kontrol Paneli**

Windows üzerinde çalışan WPF tabanlı masaüstü uygulamasıdır. Kullanıcı tarafından eklenen kamera, access point, bilgisayar, switch ve diğer IP tabanlı cihazlara ping atar; sonuçları SQLite veritabanına yazar, cihaz sağlığını izler ve ping loglarından uptime/erişilebilirlik raporu üretir.

## Özellikler

- Cihaz ekleme, düzenleme, silme, CSV import/export ve IPv4 doğrulama
- Aynı IP adresinin ikinci kez eklenmesini engelleme
- Cihaz tipi seçenekleri: Kamera, Access Point, Bilgisayar, Switch, Diğer
- Tek cihaza, filtrelenen listeye, tüm cihazlara, cihaz grubuna ve cihaz tipine göre manuel ping
- Otomatik kontrol planları: tüm cihazlar, tek cihaz, cihaz tipi, cihaz grubu ve kritik cihazlar
- Pasif planları çalıştırmayan scheduler
- Pasif veya otomatik kontrolü kapalı cihazları otomatik kontrole almama
- Aynı cihaza eş zamanlı duplicate ping atılmasını engelleyen cihaz bazlı guard
- Merkezi ping timeout ve maksimum paralel ping sınırı
- Merkezi ayarlar: global otomatik kontrol, scheduler poll aralığı, retry, retention, CSV ayırıcı ve export klasörü
- Cihaz/grup/tip/global hiyerarşik kontrol politikası
- Başarısız cihaz için sadece o cihaza özel hızlı tekrar kontrol politikası
- Ardışık hata sayacı, hızlı retry aralığı ve retry limiti
- Tek başarısız ping sonucunu doğrudan kesin arıza olarak göstermeyen durum metinleri
- Durumlar: Kontrol edilmedi, Kontrol ediliyor, Online / Sağlıklı, Uyarı, Takipte, Muhtemel erişilemiyor, Ping yanıtlamıyor olabilir
- Son başarılı kontrol, son başarısız kontrol, son kontrol zamanı ve gecikme bilgileri
- PingLog kayıtlarından 24 saat, 7 gün, 30 gün ve genel uptime/erişilebilirlik hesaplama
- UTF-8 BOM ile Excel uyumlu detaylı uptime CSV export
- Cihaz CSV import/export ve seçili cihazlar için toplu ping, otomatik kontrol aç/kapat, gruba atama, aralık uygulama ve pasifleştirme
- Dashboard, ping logları, uptime raporu, aktif kesintiler ve grup bazlı erişilebilirlik özetleri
- Sadeleştirilmiş cihaz listesi ve seçili cihaz detay paneli

## Politika Önceliği

Otomatik kontrol ve retry davranışı tek bir yerden çözümlenir. Öncelik sırası:

1. Cihaza özel override
2. Cihaz grubu varsayılanı
3. Cihaz tipi varsayılanı
4. Global ayar

Planlar hedef seçimi ve periyodik çalışma zamanlaması sağlar. Cihaz, grup veya tip seviyesinde özel aralık yoksa plan aralığı kullanılır; o da yoksa global otomatik kontrol aralığına düşülür. Cihaz detay panelinde etkin politika kaynağı kısa metin olarak gösterilir.

Policy alanları:

- Otomatik kontrol açık/kapalı
- Normal kontrol aralığı
- Ping timeout
- Hızlı retry aralığı
- Retry limiti
- Başarısızlık eşiği

## Uptime Mantığı

Uptime doğrudan sabit bir cihaz alanı olarak tutulmaz. Cihaz listesinde gösterilen uptime yüzdeleri `PingLogs` tablosundaki ölçülmüş ping sonuçlarından SQL aggregate sorgularıyla hesaplanır.

Formül:

```text
uptime = başarılı ping sayısı / ölçülen toplam ping sayısı * 100
```

Desteklenen dönemler:

- Son 24 saat
- Son 7 gün
- Son 30 gün
- Genel

Ping yanıtı alınamaması cihazın kesin kapalı olduğu anlamına gelmez. Firewall, ICMP kapatma veya ağ politikaları ping yanıtını engelleyebilir; arayüzde bu durum ayrıca belirtilir.

## Başarısız Ping ve Retry

Başarısız ping sonrası global scheduler aralığı değiştirilmez. Retry politikası cihaz bazlıdır:

- Cihazın `ConsecutiveFailures` sayacı artar.
- Cihaz, policy uygunsa sadece kendisi için hızlı retry aralığında tekrar kontrol edilir.
- Başarılı ping geldiğinde ardışık hata sayacı sıfırlanır.
- Eşik ve retry limiti doldukça durum Uyarı, Takipte ve Muhtemel erişilemiyor seviyelerine ilerler.
- Aynı cihaza aynı anda ikinci ping görevi verilmez.

Varsayılan değerler:

- Ping timeout: `1000 ms`
- Maksimum paralel ping: `32`
- Varsayılan başarısızlık eşiği: `3`
- Varsayılan hızlı retry aralığı: `60 sn`
- Varsayılan hızlı retry limiti: `3`
- Varsayılan log retention: `90 gün`
- Varsayılan scheduler poll aralığı: `15 sn`

300+ cihaz için öneri: `MaxParallelPings` değerini ağ ve cihaz kapasitesine göre `32` ile `50` arasında tutmak genellikle daha dengelidir. Çok düşük timeout değerleri yanlış negatifleri artırabilir.

## Cihaz CSV Import/Export

Cihaz listesi CSV olarak dışa aktarılabilir ve aynı formatta içe aktarılabilir. UTF-8 BOM kullanılır; varsayılan ayırıcı `;` karakteridir.

Desteklenen kolonlar:

- `Name`
- `IpAddress`
- `DeviceType`
- `GroupName`
- `Location`
- `Description`
- `AutoCheckEnabled`
- `CheckIntervalSeconds`
- `RetryIntervalSeconds`
- `RetryLimit`

Import sırasında IPv4 formatı doğrulanır. Aynı CSV içindeki duplicate IP hatalı satır olarak raporlanır. Veritabanında aynı IP varsa kullanıcı mevcut kaydı güncelleme veya satırı atlama seçimi yapar. Grup adı varsa grup otomatik oluşturulabilir; cihaz kaydı silinmeden güncellenir.

Toplu işlemler cihaz listesinde seçili satırlar üzerinden yapılır:

- Seçili cihazlara ping
- Otomatik kontrolü aç/kapat
- Gruba ata
- Özel kontrol aralığı uygula (`0` = politika devral)
- Pasifleştir

## Uptime CSV Export

Uptime ekranındaki **Uptime CSV Dışa Aktar** komutu tüm cihazlar için detaylı rapor üretir. Dosya adı şu formatta oluşturulur:

```text
NetworkHealthMonitor_UptimeReport_yyyyMMdd_HHmmss.csv
```

CSV UTF-8 BOM ile yazılır ve varsayılan ayırıcı `;` karakteridir. Rapor şu bilgileri içerir:

- Cihaz kimliği, ad, IP, tip, grup ve sağlık durumu
- Son kontrol, son başarılı kontrol, son başarısız kontrol ve gecikme
- Ardışık başarısızlık sayısı
- 24 saat, 7 gün, 30 gün ve genel uptime yüzdeleri
- Her dönem için toplam, başarılı ve başarısız kontrol sayıları

Export verisi cihaz başına log çekerek değil, `PingLogs` üzerinde aggregate SQL sorgusuyla üretilir.

## Proje Yapısı

```text
NetworkHealthMonitor/
  App.xaml
  MainWindow.xaml
  MainWindow.xaml.cs
  NetworkHealthMonitor.csproj
  NetworkHealthMonitor.sln
  Converters/
  Data/
  Infrastructure/
  Models/
  Services/
  ViewModels/
```

## Veri Saklama

Uygulama verileri kullanıcı profilinde saklanır:

```text
%LocalAppData%\NetworkHealthMonitor\network_health_monitor.db
%LocalAppData%\NetworkHealthMonitor\settings.json
```

Başlıca SQLite tabloları:

- `Devices`
- `DeviceGroups`
- `SchedulePlans`
- `PingLogs`
- `Outages`
- `AppSettings`

Veritabanı ilk çalıştırmada otomatik oluşturulur ve eksik kolonlar güvenli migration adımlarıyla eklenir.

## SQLite ve Log Retention

SQLite şu aşamada 300+ cihaz için uygundur. Uygulama `PingLogs` üzerinde `DeviceId`, `CheckedAt`, `Status` ve birleşik indexler kullanır. Uptime sorguları bu indexlerden yararlanacak şekilde aggregate SQL ile çalışır.

Log büyümesini kontrol etmek için `LogRetentionDays` ayarı vardır. Varsayılan değer `90` gündür. Uygulama açılışında düşük maliyetli retention cleanup çalışır; manuel olarak eski log temizleme de yapılabilir. Temizlik sonrası SQLite `PRAGMA optimize` çalıştırılır.

İleride log hacmi çok büyürse retention değerinin iş ihtiyacına göre düşürülmesi veya raporlama için ayrı arşivleme stratejisi eklenmesi önerilir. Çok uzun retention, birden fazla kullanıcı/servis eşzamanlı yazımı, merkezi sunucu raporlama, çok yıllı log saklama veya yoğun alarm/incident iş akışı gerekirse PostgreSQL ya da SQL Server düşünülmelidir.

300+ cihaz için önerilen başlangıç ayarları:

- `MaxParallelPings`: `32` veya ağ uygunsa `50`
- `PingTimeoutMs`: `1000-2000`
- Normal kontrol aralığı: `3-5 dakika`
- Retry interval: `30-60 saniye`
- Retry limit: `3`
- Log retention: `90 gün`

## Gelecek Kontrol Türleri

Mevcut sürüm ICMP ping odaklıdır. Tüm cihazlar ping alabildiği için TCP/HTTP/SNMP kontrolleri şu an aktif değildir. Mimari ileride `TcpPortCheckService`, `HttpCheckService` ve `SnmpCheckService` eklenebilecek şekilde ayrıştırılmıştır.

Alarm sistemi bu sürümde aktif değildir. İleride izleme sonuçlarının üstüne ayrı bir servis olarak eklenmelidir; mevcut ping/retry akışının içine gömülmemelidir.

Windows Service mimarisine bu sürümde geçilmemiştir. Scheduler, ping execution ve repository katmanları WPF ViewModel’den ayrıldığı için ileride UI ile arka plan servisinin ayrılması daha düşük riskle yapılabilir.

## Geliştirme Ortamında Çalıştırma

Geliştirme için .NET 9 SDK gerekir.

```powershell
dotnet restore
dotnet build
dotnet run
```

## Windows EXE Yayınlama

Son kullanıcı bilgisayarında .NET SDK kurulu olmasına gerek kalmaması için self-contained yayın alın:

```powershell
dotnet publish .\NetworkHealthMonitor.csproj -c Release -r win-x64 --self-contained true -o .\publish\win-x64 /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:EnableCompressionInSingleFile=true
```

Yayın çıktısı:

```text
publish\win-x64\NetworkHealthMonitor.exe
```

## Güvenlik Sınırları

Uygulama yalnızca kullanıcının manuel eklediği veya içe aktardığı IP adreslerine ping atar.

Yapılmayan işlemler:

- Subnet taraması
- Port taraması
- Şifre denemesi
- Cihazlara giriş denemesi
- Yetkisiz ağ keşfi
- Ağdaki tüm cihazları otomatik bulma
