# NetworkHealthMonitor 1.1.0 Release Notes

Bu surum final release kapilari tamamlanmadan final olarak yayinlanmamalidir. Dis ortam kabul testleri eksikse yalnizca release candidate paketi uretin.

## Degisiklikler

- Sabit aralik, gunde belirli sayida kontrol, gunluk ozel saatler ve haftalik plan modlari eklendi.
- Normal plan ile erisilemeyen cihaz yeniden kontrol araligi ayrildi.
- Varsayilan erisilemeyen cihaz yeniden kontrol araligi 20 dakika oldu.
- Eski `SchedulePlans.IntervalMinutes` kayitlari `2026071902-extended-scheduler` migration'i ile sabit aralik planina donusturulur.
- Windows Service kurulum scripti Automatic Delayed Start ve 1/5/15 dakika recovery politikasini uygular ve dogrular.
- Production readiness raporu JSON ve TXT olarak uretilir.

## Final release icin zorunlu dis kanitlar

- Gercek Windows Service kurulumu.
- Gercek reboot testi.
- Gercek SMTP gonderimi.
- Gercek ntfy bildirimi.
- UI kabul test ekran goruntuleri.
- En az 8 saat soak test.
- Upgrade ve rollback testi.
