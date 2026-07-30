# Installation Guide

1. ZIP paketini kalici bir klasore cikarin. Onerilen konum: `C:\Program Files\NetworkHealthMonitor`.
2. Yonetici PowerShell acin.
3. `Scripts\Install-NetworkHealthMonitor.ps1` veya yalnizca servis icin `Scripts\Install-WorkerService.ps1` scriptini calistirin.
4. Worker service `Manual / Demand Start` olarak kurulur ve kurulum scripti servisi otomatik baslatmaz.
5. Normal kullanici oturumunda `Tray\NetworkHealthMonitor.Tray.exe` ile sistem tepsisi uygulamasini acin.
6. Yonetim paneli `UI\NetworkHealthMonitor.exe` ile veya tray menusunden acilir.

Veri konumu: `C:\ProgramData\NetworkHealthMonitor`.
Veritabani: `C:\ProgramData\NetworkHealthMonitor\data\NetworkHealthMonitor.db`.

Tray varsayilan olarak Windows baslangicina eklenmez ve Worker'i otomatik baslatmaz. Bu iki davranis tray ayarlarinda ayri seceneklerdir ve varsayilanlari kapalidir.
