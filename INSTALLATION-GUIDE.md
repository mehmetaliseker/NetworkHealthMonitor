# Installation Guide

1. ZIP paketini kalici bir klasore cikarin. Onerilen konum: `C:\Program Files\NetworkHealthMonitor`.
2. Yonetici PowerShell acin.
3. `scripts\install-service.ps1` scriptini calistirin.
4. Script servis icin `Automatic (Delayed Start)` ve recovery politikasini uygular.
5. UI uygulamasini normal kullanici olarak `ui\NetworkHealthMonitor.exe` ile acin.

Veri konumu: `C:\ProgramData\NetworkHealthMonitor`.

Kurulumdan sonra normal kullanici UI'i yonetici olmadan kullanabilir. Service kurulumu, kaldirma ve upgrade islemleri yonetici yetkisi gerektirir.
