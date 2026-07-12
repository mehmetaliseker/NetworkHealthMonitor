# NetworkHealthMonitor Server Kurulumu

Bu paket Windows Server üzerinde WPF yönetim arayüzü ve kullanıcı oturumundan bağımsız çalışan Windows Service worker bileşeninden oluşur.

## Desteklenen ortam

- Windows Server 2019, Windows Server 2022 veya Windows Server 2025 x64
- ICMP echo/ping trafiğine izin veren ağ ve firewall kuralları
- Kurulum, başlatma ve kaldırma için yönetici PowerShell oturumu
- Paket self-contained üretildiği için hedef sunucuda .NET SDK gerekmez.

## Paket konumu

ZIP dosyasını örneğin aşağıdaki klasöre açın:

```powershell
C:\Program Files\NetworkHealthMonitor
```

Beklenen içerik:

```text
ui\NetworkHealthMonitor.exe
worker\NetworkHealthMonitor.Worker.exe
scripts\install-service.ps1
scripts\uninstall-service.ps1
scripts\start-service.ps1
scripts\stop-service.ps1
scripts\service-status.ps1
README-SERVER.md
```

## Veri dizini

UI ve Windows Service aynı veriyi kullanır:

```text
C:\ProgramData\NetworkHealthMonitor\network_health_monitor.db
C:\ProgramData\NetworkHealthMonitor\settings.json
C:\ProgramData\NetworkHealthMonitor\logs\
C:\ProgramData\NetworkHealthMonitor\backups\
```

Eski kullanıcı bazlı veri dizini:

```text
%LocalAppData%\NetworkHealthMonitor
```

Yeni ProgramData veritabanı yoksa ve eski LocalAppData veritabanı varsa uygulama eski veritabanını doğrular, ProgramData altına kopyalar ve ProgramData `backups` klasörüne yedek alır. Eski dosya silinmez.

## Servis kurulumu

Yönetici PowerShell açın, paketin kök klasörüne gidin ve çalıştırın:

```powershell
Set-Location "C:\Program Files\NetworkHealthMonitor"
.\scripts\install-service.ps1
.\scripts\start-service.ps1
.\scripts\service-status.ps1
```

Servis bilgileri:

```text
Servis adı: NetworkHealthMonitorWorker
Görünen ad: Network Health Monitor Worker
Başlangıç tipi: Delayed Automatic
Recovery: hata sonrası yeniden başlatma
```

Servis çalışırken WPF arayüzünü açmak için:

```powershell
.\ui\NetworkHealthMonitor.exe
```

Arayüz kapatıldığında veya kullanıcı oturumu kapandığında Windows Service çalışmaya devam eder. Sunucu yeniden başladıktan sonra servis otomatik başlar ve aktif planları SQLite veritabanından yeniden yükler.

## Servis güncelleme

1. Yönetici PowerShell açın.
2. Eski servisi durdurun:

```powershell
.\scripts\stop-service.ps1
```

3. Yeni paket içeriğini aynı klasöre kopyalayın.
4. Servisi güncelleyin ve başlatın:

```powershell
.\scripts\install-service.ps1
.\scripts\start-service.ps1
.\scripts\service-status.ps1
```

Kurulum scripti var olan servisi körlemesine silmez; binary path, başlangıç tipi ve recovery ayarlarını günceller.

## Servisi kaldırma

```powershell
.\scripts\stop-service.ps1
.\scripts\uninstall-service.ps1
```

Bu işlem ProgramData altındaki veritabanını, ayarları, logları veya yedekleri silmez.

## ICMP yanıtı alınamıyorsa

- Hedef cihaz ping yanıtı veriyor mu kontrol edin.
- Windows Firewall veya ağ firewall ICMP Echo Request/Reply trafiğini engelliyor olabilir.
- Sunucu ve hedef cihaz arasında routing/VLAN erişimi var mı kontrol edin.
- Uygulama subnet taraması, port taraması, SNMP, HTTP/TCP kontrolü veya otomatik keşif yapmaz; yalnızca kullanıcı tarafından eklenen IP adreslerine standart ICMP ping gönderir.

## Yedeklenecek dosyalar

Düzenli yedek için en az şu dosya ve klasörleri saklayın:

```text
C:\ProgramData\NetworkHealthMonitor\network_health_monitor.db
C:\ProgramData\NetworkHealthMonitor\settings.json
C:\ProgramData\NetworkHealthMonitor\logs\
C:\ProgramData\NetworkHealthMonitor\backups\
```

## Arayüz ve servis ayrımı

- WPF arayüz cihaz, grup, plan, log, CSV ve manuel ping işlemleri içindir.
- Otomatik planlı ping yalnızca `NetworkHealthMonitorWorker` servisi tarafından çalıştırılır.
- Tray ikonundan arayüz geri açılabilir, servis durumu görülebilir ve arayüz kapatılabilir.
- Arayüzden çıkmak worker servisini durdurmaz.
