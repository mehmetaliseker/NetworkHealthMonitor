# Test Summary

Bu dosya release paketine otomatik test ozeti icin eklenir.

Son otomatik test komutu:

```powershell
dotnet test NetworkHealthMonitor.sln -c Debug --no-build
```

Gercek service, reboot, SMTP, ntfy, UI kabul ve soak testleri `release\verification` altinda ayri JSON/TXT kanitlarla saklanmalidir. Bu kanitlar yoksa paket final degil release candidate kabul edilmelidir.
