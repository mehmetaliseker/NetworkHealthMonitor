# Changelog

## 1.1.0
- Izleme planlari genisletildi: sabit aralik, gunde belirli sayida kontrol, gunluk ozel saatler ve haftalik plan desteklenir.
- Eski `IntervalMinutes` planlari geriye uyumlu olarak yeni sabit aralik modeline migrate edilir.
- Erisilemeyen cihazlar icin normal plandan ayri yeniden kontrol araligi eklendi; varsayilan deger 20 dakikadir.
- Worker scheduler ayni cevrimde ayni cihaz icin duplicate ping baslatmayacak sekilde normal plan ve offline recheck akisini birlestirir.
- Windows Service kurulum scripti Automatic Delayed Start ve 1/5/15 dakika recovery politikasini dogrular.
- Production readiness ve release paket scriptleri eklendi; dis kabul raporlari olmadan final release uretilmez.

## 1.0.0
- net10.0-windows hedefi.
- ProgramData ortak veri yolu: data/config/logs/backups.
- Worker-owned scheduler, heartbeat ve notification dispatcher.
- SQLite soft-delete, incident ve notification outbox tablolari.
- ntfy ayarlari, DPAPI token korumasi ve test bildirimi.
- E-posta/SMTP kanali, alici gruplari, sablon onizleme ve test e-postasi.
- Cihaz kesinti incident modeli, duplicate korumali ilk bildirim ve escalation.
- Bildirimleri susturma ve izlemeyi gecici durdurma modlari.
- WPF arayuzunde yeni sol navigasyon, genel bakis, cihazlar, olaylar, bildirimler ve ayarlar bilgi mimarisi.
- Cihaz silme, toplu silme, silinenleri filtreleme ve geri yukleme.
- Windows service, backup, restore, upgrade ve health-check scriptleri.
- Release hardening: UI read-only binding hatalari giderildi, migration yedegi yalnizca gerekli sema gecislerinde alinir, ProgramData ACL'i sinirlandirildi.
