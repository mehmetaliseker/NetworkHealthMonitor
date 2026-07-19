# Backup and Restore

Backup almak icin:

```powershell
.\scripts\backup-data.ps1
```

Restore icin:

```powershell
.\scripts\restore-data.ps1 -BackupPath "C:\Path\backup.zip"
```

Restore oncesinde Worker servisini durdurun. `uninstall-service.ps1` varsayilan olarak kullanici verisini silmez. Veriyi silmek icin acikca `-RemoveData` kullanilmalidir.
