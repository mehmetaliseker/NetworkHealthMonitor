# NetworkHealthMonitor Sekme Rehberi

Son güncelleme: 2026-08-05

Bu dosya UI içindeki ana sekmelerin ne yaptığını ve hangi ayarların hangi akışı etkilediğini açıklar. Uygulama otomatik olarak worker başlatmaz; planlı kontrollerin loglara yazılması için Worker servisinin veya Worker prosesinin çalışıyor olması gerekir.

## Shell

`MainWindow.xaml` ana kabuğu `Views/Shell/MainShellView.xaml` üzerinden açar.

| Alan | Görev |
| --- | --- |
| Sol menü | Ana sayfalar arasında geçiş yapar. Kapalıyken `N` düğmesiyle açılır, açıkken burger düğmesiyle kapanır. |
| Üst bar | Aktif sayfa başlığı, açıklaması, geri ve yenile komutlarını gösterir. |
| İçerik alanı | Seçilen sayfanın ViewModel ve View eşleşmesini gösterir. |
| Alt durum barı | UI durumu, servis durumu ve son işlem mesajını gösterir. |
| Toast/dialog alanları | Kullanıcı işlemleri için kısa mesaj ve onay pencerelerini gösterir. |

## Ana Sekmeler

| Sıra | Sekme | Ne için kullanılır |
| --- | --- | --- |
| 1 | Genel | Günlük kullanım ekranıdır. Tüm cihazlara, seçili gruba veya seçili cihaz türüne anlık ping atılır. İlk başarısız pingden itibaren yanıt vermeyen/takipte cihazlar renkli listede görünür. |
| 2 | Cihazlar | Tüm cihazları listeler. Cihaz ekleme, düzenleme, silme, CSV içe/dışa aktarma, toplu aktif/pasif ve toplu ping işlemleri buradan yapılır. Liste IP, ad ve durum bilgisini önde gösterir. |
| 3 | Loglar | Ping geçmişini tarih, cihaz, grup ve durum ile filtreler. Ping kayıtlarını CSV olarak dışa aktarır. |
| 4 | Cihaz Grupları | Grup tanımlama, grup cihazlarını görme ve seçili gruba ping atma akışını içerir. |
| 5 | Canlı Durum | Çevrimiçi cihazları, ilk hata dahil yanıt vermeyen cihazları, kontrol edilen cihazları ve son durum değişikliklerini gösterir. |
| 6 | Kontrol Planları | Planlı ping kuralları, hedefleri, aralıkları ve retry davranışını ayarlar. |
| 7 | Kesintiler | Açık, kapalı ve geçmiş kesintileri takip eder. Incident açma/kapatma kuralları ping sonuçları ve failure eşikleriyle belirlenir. |
| 8 | Bildirimler | Outbox kayıtlarını, gönderim durumlarını ve bildirim geçmişini izler. SMTP ve ntfy kanalları bağımsız denenir. |
| 9 | Raporlar | Uptime, kesinti süresi, SLA, MTTR/MTBF, ping performansı ve bildirim başarı raporlarını üretir. |
| 10 | Worker Servisi | Worker durumunu görür, servisi başlatır/durdurur/yeniden başlatır/kurar/kaldırır ve otomatik başlatma ayarını yönetir. Yönetici yetkisi gerektiren işlemler yetkisiz ortamda başarılı kabul edilmez. |
| 11 | Sistem Sağlığı | UI, worker, tray, SQLite, scheduler, log ve sistem hazırlık durumunu gösterir. |
| 12 | Ayarlar | Genel, ping/retry, ntfy, SMTP, alıcılar, bildirim şablonları, veri saklama ve gelişmiş ayarları içerir. |
| Alt | Yardım | Kullanım notlarını ve destek metinlerini gösterir. |
| Alt | Hakkında | Sürüm, bileşen ve ortam bilgilerini gösterir. |

## Worker ve Planlı Kontrol Akışı

1. Planlı kontroller `Kontrol Planları` ve cihaz/grup/tür ayarlarına göre hesaplanır.
2. Worker çalışıyorsa zamanı gelen cihazları seçer.
3. Her cihaz için ping çalıştırılır ve ping sonucu loglara yazılır.
4. İlk başarısız ping UI tarafında `Uyarı` veya `Yanıt alınamadı` olarak görünür.
5. Retry süreci devam ederken cihaz takipte kalır.
6. Ayarlanan offline eşiğine ulaşıldığında cihaz uzun süre ulaşılamıyor kabul edilir ve kırmızı durum gösterilir.
7. Incident/outbox akışı aynı kurallarla çalışır; bildirim hatası scheduler döngüsünü durdurmamalıdır.
8. Worker kapalıysa planlı kontrol log üretmez. Manuel ping komutları UI üzerinden çalışmaya devam eder.

## Renkler

| Durum | Renk anlamı |
| --- | --- |
| Erişilebilir | Yeşil; cihaz yanıt verdi. |
| Uyarı / Yanıt alınamadı / Takipte | Sarı, turuncu veya mavi tonları; ilk hata ve doğrulama süreci. |
| Erişilemiyor | Kırmızı; uzun süreli veya eşiği geçmiş erişim hatası. |
| Kontrol edilmedi | Nötr; henüz ping sonucu yok. |

## Tray

`NetworkHealthMonitor.Tray.exe` çalışıyorsa Windows görev çubuğundaki gizli simgeler alanında görünür. Buradan UI açma ve worker yönetimi yapılabilir. Tray otomatik başlatma ayrı ayardır; worker otomatik olarak başlatılmaz.

## Alt Sekmeler

| Üst sekme | Alt sekme | Ne yapar |
| --- | --- | --- |
| Cihaz Detayı | Genel | Seçili cihazın temel bilgilerini ve son durumunu gösterir. |
| Cihaz Detayı | Kontrol Geçmişi | Seçili cihazın ping geçmişini gösterir. |
| Cihaz Detayı | Kesintiler | Seçili cihaza ait kesinti kayıtlarını gösterir. |
| Cihaz Detayı | Bildirimler | Seçili cihaza ait outbox/bildirim kayıtlarını gösterir. |
| Cihaz Detayı | Ayarlar | Cihaz özel ping, retry, eşik ve izleme ayarlarını gösterir. |
| Worker Servisi | Durum | Servis durumu, başlangıç türü, heartbeat, son scheduler döngüsü, son planlı ping ve bildirim özetlerini gösterir. |
| Worker Servisi | Çalıştırma | Servis başlatma/durdurma/yeniden başlatma/kurma/kaldırma ve worker otomatik başlatma seçeneğini içerir. |
| Sistem Sağlığı | Genel Sağlık | UI, worker, tray ve SQLite özet durumunu gösterir. |
| Sistem Sağlığı | Veritabanı | SQLite dosyası, WAL ve bağlantı sağlığını gösterir. |
| Sistem Sağlığı | Scheduler | Planlı kontrol ve son scheduler çalışmasını gösterir. |
| Sistem Sağlığı | Loglar | Uygulama ve worker log sağlık bilgisini gösterir. |

