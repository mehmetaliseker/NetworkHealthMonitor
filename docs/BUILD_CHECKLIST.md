# Build ve Yayın Kontrol Listesi

Bu liste build alınabilen Windows ortamında uygulanmalıdır.

## 1. Gerekli Ortam

- Windows
- .NET 9 SDK
- Proje kaynak kodu

## 2. Komutlar

```powershell
dotnet restore
dotnet build -c Release
dotnet run
dotnet publish -c Release -r win-x64 --self-contained true
```

## 3. İlk Açılış Kontrolü

- Uygulama hatasız açılıyor mu?
- Veritabanı ve ayar dosyası otomatik oluşuyor mu?
- Ana dashboard boş veride hata vermiyor mu?

## 4. Settings Kontrolü

- Ayarlar kaydediliyor mu?
- Uygulama kapatılıp açıldığında ayarlar korunuyor mu?
- Geçersiz değerler min/max kontrolleriyle yakalanıyor mu?

## 5. SQLite Migration Kontrolü

- Eski veritabanı açıldığında eksik kolonlar ekleniyor mu?
- Index oluşturma adımları hata vermiyor mu?
- WAL ve `busy_timeout` aktif mi?

## 6. Cihaz CRUD Kontrolü

- Cihaz ekleme, düzenleme ve silme çalışıyor mu?
- Duplicate IP engelleniyor mu?
- Geçersiz IP uyarı veriyor mu?

## 7. Manuel Ping Kontrolü

- Tek cihaz ping çalışıyor mu?
- Filtreli liste ve tüm cihaz ping çalışıyor mu?
- Grup ve tip bazlı ping sonuçları loglara yazılıyor mu?

## 8. Otomatik Ping Kontrolü

- Aktif plan zamanı gelince çalışıyor mu?
- Pasif planlar çalışmadan kalıyor mu?
- Pasif veya auto check kapalı cihazlar otomatik kontrolden hariç tutuluyor mu?

## 9. Akıllı Retry Kontrolü

- Başarısız cihaz için yalnız ilgili cihazın retry zamanı kısalıyor mu?
- Global scheduler aralığı değişmeden kalıyor mu?
- Başarılı ping sonrası failure sayacı sıfırlanıyor mu?

## 10. Policy Öncelik Kontrolü

- Cihaz özel ayarı grup/tip/global ayarların önüne geçiyor mu?
- Grup ayarı tip/global ayarların önüne geçiyor mu?
- Tip ayarı global ayarın önüne geçiyor mu?
- Cihaz detay paneli etkin policy kaynağını doğru gösteriyor mu?

## 11. Uptime Kontrolü

- 24 saat, 7 gün, 30 gün ve genel uptime değerleri görünüyor mu?
- Değerler PingLog kayıtlarından hesaplanıyor mu?
- Log olmayan cihazda yanıltıcı yüzde gösterilmiyor mu?

## 12. Uptime CSV Export Kontrolü

- CSV dosyası tarih/saat içeren adla oluşuyor mu?
- Türkçe karakterler Excel'de düzgün mü?
- Tüm beklenen kolonlar var mı?

## 13. Cihaz CSV Import/Export Kontrolü

- Şablon üretilebiliyor mu?
- Export edilen dosya tekrar import edilebilir formatta mı?
- Duplicate IP için güncelle/atla/iptal seçimi çalışıyor mu?
- Hatalı satırlar raporlanıyor mu?

## 14. Toplu İşlem Kontrolü

- Seçili cihazlara ping çalışıyor mu?
- Auto check aç/kapat, gruba ata, aralık uygula ve pasifleştir komutları onay istiyor mu?
- İşlem sonrası liste ve detay paneli güncelleniyor mu?

## 15. 300+ Cihaz Performans Kontrolü

- 300+ cihaz import edilebiliyor mu?
- Toplu ping `MaxParallelPings` sınırına uyuyor mu?
- Dashboard, filtreler ve detay paneli kullanılabilir kalıyor mu?
- Uptime CSV export makul sürede tamamlanıyor mu?

## 16. UI Taşma/Sığma Kontrolü

- Uzun cihaz adı, grup ve açıklama metinleri tabloyu bozmuyor mu?
- Tooltip ve text trimming çalışıyor mu?
- Küçük pencere boyutunda ana akış kullanılabilir mi?

## 17. Publish Klasörü ile Başka Bilgisayarda Çalıştırma Kontrolü

- Publish çıktısı farklı Windows bilgisayarda açılıyor mu?
- Veritabanı ve ayarlar kullanıcı profilinde oluşuyor mu?
- Ping, CSV export ve ayar kaydetme çalışıyor mu?
