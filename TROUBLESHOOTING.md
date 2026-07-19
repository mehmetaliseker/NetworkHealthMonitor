# Troubleshooting

## Worker calismiyor

`scripts\service-status.ps1` ve `scripts\service-health-check.ps1` komutlarini calistirin. Service kurulu degilse `install-service.ps1` gereklidir.

## SMTP e-posta gonderilmiyor

SMTP baglanti testini ve test e-postasini ayri ayri calistirin. Kimlik dogrulama, port, guvenlik modu ve gonderen adresini kontrol edin.

## ntfy bildirimi gitmiyor

Sunucu URL'i, topic ve token bilgisini kontrol edin. Gercek subscriber ile test bildirimi alin.

## SQLite locked

UI ve Worker'in ayni veritabanini kullandigini, dis antivirus/backup yaziliminin dosyayi kilitlemedigini kontrol edin. Worker loglarinda tekrar eden lock hatasi varsa production readiness raporunu inceleyin.

## Migration basarisiz

Worker ping'e baslamamalidir. Son otomatik yedek `C:\ProgramData\NetworkHealthMonitor\backups` altindadir. Hata metniyle birlikte veritabanini yedekten geri yukleyin.
