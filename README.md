# Network Health Monitor

Türkçe arayüz başlığı: **Ağ Cihazları Kontrol Paneli**

NetworkHealthMonitor, Windows üzerinde çalışan WPF tabanlı bir ağ izleme uygulamasıdır. Kullanıcı tarafından eklenen kamera, access point, bilgisayar, switch ve diğer IP tabanlı cihazlara ICMP ping atar; sonuçları SQLite veritabanına yazar, cihaz sağlığını izler ve uptime raporları üretir.

Bu sürüm profesyonel şirket içi kullanım için sade tutulmuştur: alarm sistemi, HTTP/TCP/SNMP aktif kontrolleri, Windows Service, installer, web panel ve kullanıcı/rol sistemi bu aşamada eklenmemiştir.

## Öne Çıkanlar

- Tek cihaz, seçili cihazlar, grup, tip, filtreli liste ve tüm cihazlar için manuel ping
- Aktif/pasif plan ayrımı yapan otomatik scheduler
- Duplicate ping guard ve merkezi maksimum paralel ping sınırı
- Cihaz/grup/tip/global hiyerarşik kontrol politikası
- Başarısız cihaz için global scheduler'ı hızlandırmadan cihaz bazlı akıllı retry
- PingLog kayıtlarından 24 saat, 7 gün, 30 gün ve genel uptime hesaplama
- UTF-8 BOM ile Excel uyumlu uptime CSV export
- Cihaz CSV import/export, duplicate IP kontrolü ve import sonuç raporu
- Seçili cihazlar için toplu ping, auto check aç/kapat, gruba ata, aralık uygula ve pasifleştir
- SQLite WAL, busy timeout, indexler, retention cleanup ve manuel optimize
- Dashboard, sade cihaz listesi ve cihaz detay paneli

## Dokümantasyon

- [Mimari Notları](docs/ARCHITECTURE.md)
- [Kullanıcı Kılavuzu](docs/USER_GUIDE.md)
- [Test Planı](docs/TEST_PLAN.md)
- [Build ve Yayın Kontrol Listesi](docs/BUILD_CHECKLIST.md)

## Veri Saklama

Uygulama verileri kullanıcı profilinde saklanır:

```text
%LocalAppData%\NetworkHealthMonitor\network_health_monitor.db
%LocalAppData%\NetworkHealthMonitor\settings.json
```

## Geliştirme Ortamında Çalıştırma

Geliştirme için Windows ve .NET 9 SDK gerekir.

```powershell
dotnet restore
dotnet build -c Release
dotnet run
```

Self-contained yayın için:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

Build öncesi ayrıntılı kontrol adımları için [Build ve Yayın Kontrol Listesi](docs/BUILD_CHECKLIST.md) dosyasını kullanın.

## 300+ Cihaz İçin Başlangıç Önerileri

- `MaxParallelPings`: 32 veya ağ uygunsa 50
- `PingTimeoutMs`: 1000-2000 ms
- Normal kontrol aralığı: 3-5 dakika
- Retry interval: 30-60 saniye
- Retry limit: 3
- Log retention: 90 gün

## Sınırlar ve Gelecek Genişleme

Mevcut sürüm ICMP ping odaklıdır. Tüm cihazlar ping alabildiği için TCP/HTTP/SNMP kontrolleri şu an aktif değildir. Mimari ileride `TcpPortCheckService`, `HttpCheckService` ve `SnmpCheckService` eklenebilecek şekilde ayrıştırılmıştır.

Alarm sistemi bu sürümde aktif değildir. İleride ping/outage sonuçlarını dinleyen ayrı bir servis olarak eklenmelidir; mevcut ping ve retry akışının içine gömülmemelidir.

Windows Service mimarisine bu sürümde geçilmemiştir. Scheduler, ping execution, policy ve repository katmanları ayrıldığı için ileride UI ile arka plan servisi ayrımı daha düşük riskle yapılabilir.

## Güvenlik Sınırları

Uygulama yalnızca kullanıcının manuel eklediği veya CSV ile içe aktardığı IP adreslerine ping atar.

Yapılmayan işlemler:

- Subnet taraması
- Port taraması
- Şifre denemesi
- Cihazlara giriş denemesi
- Yetkisiz ağ keşfi
- Ağdaki tüm cihazları otomatik bulma
