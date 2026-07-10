# NetworkHealthMonitor Test Planı

Bu plan, build alınabilen ortamda profesyonel kullanım öncesi elle doğrulama için hazırlanmıştır. Testlerde mümkünse gerçek ağdan bağımsız küçük bir deneme veritabanı ve ardından 300+ cihazı temsil eden ayrı bir performans verisi kullanılmalıdır.

## 1. Uygulama ilk açılış testi

Amaç: Uygulamanın temiz kurulumda, mevcut veritabanıyla ve geçersiz ayar dosyası senaryosunda açılış davranışını doğrulamak.

Adımlar:
- Temiz kullanıcı klasöründe uygulamayı aç.
- Mevcut SQLite veritabanı ile tekrar aç.
- `settings.json` dosyasını geçersiz JSON yapıp uygulamayı aç.

Beklenen sonuç:
- Uygulama ana ekranı hatasız açılır.
- Veritabanı şeması güvenli migration ile hazırlanır.
- Ayar dosyası okunamazsa kullanıcıya anlaşılır uyarı verilir ve varsayılan ayarlar yüklenir.

Riskli noktalar:
- SQLite migration hatası uygulama açılışını engelleyebilir.
- Bozuk ayar dosyası ham exception olarak gösterilmemelidir.

## 2. Cihaz ekleme/düzenleme/silme testi

Amaç: Zorunlu alan, duplicate IP, policy override ve silme onaylarını doğrulamak.

Adımlar:
- Geçerli ad, IP, tip ve grup ile cihaz ekle.
- Duplicate IP ile ikinci cihaz eklemeyi dene.
- Cihazı düzenleyip lokasyon, açıklama, timeout, retry ve interval alanlarını değiştir.
- Cihaz silme işlemini onaylamadan iptal et, sonra onaylayarak sil.

Beklenen sonuç:
- Geçerli cihaz kaydedilir.
- Duplicate IP anlaşılır mesajla engellenir.
- Policy alanları kaydedilip cihaz detayında görünür.
- Silme işlemi onaysız gerçekleşmez.

Riskli noktalar:
- Silme ping loglarını yanlışlıkla temizlememelidir.
- Boş veya geçersiz IP kayıt edilmemelidir.

## 3. Manuel ping testleri

Amaç: Tek cihaz, seçili cihazlar, grup, tip ve tüm cihaz manuel ping akışını doğrulamak.

Adımlar:
- Tek cihaz satırından ping çalıştır.
- Birden fazla cihaz seçip toplu ping çalıştır.
- Grup ve tip bazlı ping komutlarını çalıştır.
- Tüm cihaz ping komutunu çalıştır.

Beklenen sonuç:
- Ping sonuçları cihaz listesine ve loglara yansır.
- Aynı cihaza eş zamanlı duplicate ping düşmez.
- UI ping sırasında kullanılabilir kalır.

Riskli noktalar:
- Çoklu pingde `MaxParallelPings` sınırı aşılmamalıdır.
- Pasif cihazlar toplu otomatik akışa karışmamalıdır.

## 4. Otomatik ping testleri

Amaç: Scheduler planlarının aktif/pasif, hedef çözümleme ve policy uyumunu doğrulamak.

Adımlar:
- Aktif bir otomatik plan oluştur.
- Pasif plan oluşturup zamanı gelse bile çalışmadığını izle.
- Grup, tip ve tüm cihaz hedefli planları test et.
- Global otomatik kontrol ayarını kapatıp scheduler davranışını gözle.

Beklenen sonuç:
- Yalnız aktif planlar zamanı gelince çalışır.
- Pasif planlar çalışmaz.
- Auto check kapalı cihazlar otomatik kontrol dışında kalır.

Riskli noktalar:
- Scheduler global intervali başarısız cihazlar nedeniyle hızlanmamalıdır.
- Her tick'te pahalı cleanup veya tam veri yenileme yapılmamalıdır.

## 5. Akıllı retry testi

Amaç: Başarısız ping sonrası sadece ilgili cihazın kısa retry akışına girdiğini doğrulamak.

Adımlar:
- Test cihazının ping yanıtını geçici olarak başarısız hale getir.
- İlk başarısızlıktan sonra cihaz durumunu ve `NextCheckAt` zamanını izle.
- Retry limitine kadar başarısızlığı sürdür.
- Ping yanıtını tekrar başarılı hale getir.

Beklenen sonuç:
- Sadece başarısız cihazın tekrar kontrol zamanı kısalır.
- Diğer cihazların normal aralığı etkilenmez.
- Failure count başarı sonrası sıfırlanır ve cihaz normal aralığa döner.

Riskli noktalar:
- Retry sonsuz döngüye girmemelidir.
- Tek başarısız ping kesin arıza gibi gösterilmemelidir.

## 6. Policy öncelik testi

Amaç: Cihaz > grup > tip > global önceliğinin tüm ping ve UI akışlarında tutarlı olduğunu doğrulamak.

Adımlar:
- Global timeout ve retry değerlerini kaydet.
- Tip bazlı farklı değer gir.
- Grup bazlı farklı değer gir.
- Cihaza özel override gir.
- Her seviyeyi sırayla boşaltıp etkin policy metnini ve ping davranışını kontrol et.

Beklenen sonuç:
- En özel dolu değer kullanılır.
- Cihaz detayında etkin politika kaynağı doğru görünür.
- Manuel ve otomatik ping aynı policy çözümlemesini kullanır.

Riskli noktalar:
- CSV import veya toplu işlem sonrası cihaz override değerleri beklenmedik şekilde ezilmemelidir.
- UI'da görünen policy ile servislerin kullandığı policy ayrışmamalıdır.

## 7. Uptime hesaplama testi

Amaç: Uptime değerlerinin PingLog kayıtlarından dönemsel aggregate mantıkla hesaplandığını doğrulamak.

Adımlar:
- Test cihazı için başarılı ve başarısız ping logları üret.
- 24 saat, 7 gün, 30 gün ve genel uptime değerlerini kontrol et.
- Logları tarih aralıklarına göre değiştirip tekrar hesaplat.

Beklenen sonuç:
- Formül başarılı kontrol / toplam kontrol * 100 şeklinde tutarlı çalışır.
- Dönemsel toplam, başarılı ve başarısız sayılar doğru hesaplanır.
- UI thread uzun hesaplamada kilitlenmez.

Riskli noktalar:
- Tüm PingLog tablosu belleğe çekilmemelidir.
- Log olmayan cihazlarda yanıltıcı yüzde gösterilmemelidir.

## 8. Uptime CSV export testi

Amaç: Uptime raporunun Excel uyumlu ve performanslı dışa aktarıldığını doğrulamak.

Adımlar:
- Export klasörünü ayarlardan belirle.
- Uptime CSV dışa aktar komutunu çalıştır.
- Oluşan dosyayı Excel ile aç.
- Yazma izni olmayan klasör senaryosunu dene.

Beklenen sonuç:
- Dosya adı tarih/saat içerir.
- UTF-8 BOM ve ayarlanan ayırıcı kullanılır.
- Tüm beklenen uptime ve kontrol sayısı kolonları bulunur.
- Hata durumunda anlaşılır mesaj gösterilir.

Riskli noktalar:
- Export UI'yı kilitlememelidir.
- Türkçe karakterler Excel'de bozulmamalıdır.

## 9. Cihaz CSV import/export testi

Amaç: Cihaz listesinin veri kaybı oluşturmadan içe/dışa aktarılmasını doğrulamak.

Adımlar:
- CSV şablonu oluştur.
- Geçerli cihaz satırlarıyla import yap.
- Duplicate IP, geçersiz IP, eksik ad ve bilinmeyen tip içeren satırları ekleyip import yap.
- Mevcut IP için güncelle/atla/iptal seçimlerini ayrı ayrı test et.
- Cihaz export alıp import şablonu ile kolon uyumunu kontrol et.

Beklenen sonuç:
- Geçerli satırlar eklenir veya kullanıcı seçimine göre güncellenir.
- Duplicate IP kullanıcı kararı olmadan ezilmez.
- Geçersiz satırlar raporda görünür.
- CSV Türkçe karakterleri korur.

Riskli noktalar:
- Grup eşleşmesi yanlış yapılmamalıdır.
- Export edilen kolonlar import tarafından okunabilir kalmalıdır.

## 10. Toplu işlem testi

Amaç: Seçili cihazlara uygulanan toplu işlemlerin onay, kapsam ve sonuç raporunu doğrulamak.

Adımlar:
- Hiç cihaz seçmeden her toplu komutu dene.
- Birkaç cihaz seçip seçilileri ping komutunu çalıştır.
- Auto check aç/kapat, gruba ata, aralık uygula ve pasifleştir işlemlerini ayrı ayrı dene.
- Onay penceresinde iptal ve onay seçeneklerini test et.

Beklenen sonuç:
- Seçim yoksa işlem başlamaz.
- Mutasyon yapan işlemler etkilenecek cihaz sayısını gösterir.
- Pasifleştirme onaysız yapılmaz.
- İşlem sonrası liste ve detay paneli güncellenir.

Riskli noktalar:
- Toplu işlem yanlışlıkla tüm cihazlara uygulanmamalıdır.
- Pasif cihazlar otomatik/retry akışına girmemelidir.

## 11. SQLite retention/cleanup testi

Amaç: Log saklama, manuel cleanup ve optimize bakımının güvenli çalıştığını doğrulamak.

Adımlar:
- Farklı tarihlerde PingLog kayıtları oluştur.
- Log retention gününü ayarlardan değiştir.
- Eski logları temizle komutunu çalıştır.
- Veritabanını optimize et komutunu çalıştır.

Beklenen sonuç:
- Retention dışındaki eski loglar silinir.
- Cihaz kayıtları korunur.
- Optimize komutu kullanıcıya anlaşılır sonuç verir.

Riskli noktalar:
- Cleanup her scheduler tick'inde çalışmamalıdır.
- SQLite lock durumları ham exception olarak gösterilmemelidir.

## 12. 300+ cihaz performans testi

Amaç: 300+ cihaz ve büyüyen PingLog tablosunda UI, scheduler ve rapor performansını doğrulamak.

Adımlar:
- 300 veya daha fazla cihazı CSV ile import et.
- Toplu ping ve otomatik plan çalıştır.
- Uptime raporu ve log filtrelerini kullan.
- Büyük PingLog tablosunda cleanup ve export işlemlerini dene.

Beklenen sonuç:
- Pingler `MaxParallelPings` sınırıyla çalışır.
- UI temel etkileşimlerde kilitlenmez.
- Uptime ve CSV export aggregate sorgularla makul sürede tamamlanır.

Riskli noktalar:
- ObservableCollection çok sık tam yenilenirse UI yavaşlayabilir.
- PingLog indexleri eksikse uptime ve export yavaşlar.

## 13. Settings kaydet/yükle testi

Amaç: Merkezi ayarların kalıcı saklandığını, normalize edildiğini ve servisler tarafından kullanıldığını doğrulamak.

Adımlar:
- Ping timeout, paralellik, scheduler sıklığı, retry, retention, CSV ayırıcı ve export klasörünü değiştir.
- Ayarları kaydet.
- Uygulamayı kapatıp aç.
- Manuel/otomatik ping ve export işlemlerinde yeni değerleri doğrula.

Beklenen sonuç:
- Ayarlar yeniden açılışta korunur.
- Min/max dışındaki değerler kullanıcıya uyarı verir veya normalize edilir.
- Servisler son kaydedilen ayarları kullanır.

Riskli noktalar:
- UI'da görünen ayar ile servis davranışı ayrışmamalıdır.
- Geçersiz ayar dosyası varsayılanlara güvenli dönmelidir.

## 14. UI taşma/sığma testi

Amaç: Uzun cihaz adı, grup, lokasyon ve açıklama metinlerinde ana ekranın okunabilir kaldığını doğrulamak.

Adımlar:
- Uzun ad, uzun grup adı, uzun açıklama ve farklı ekran genişlikleriyle cihaz listesine bak.
- Dashboard, filtreler, cihaz listesi ve detay panelini küçük pencere boyutunda kontrol et.
- Ayarlar ekranında tüm grupların erişilebilir olduğunu doğrula.

Beklenen sonuç:
- Ana tablo kolonları taşmadan okunur kalır.
- Uzun metinler kırpılır veya tooltip ile tam gösterilir.
- Teknik bilgiler ana tabloyu kalabalıklaştırmadan detay panelinde görünür.

Riskli noktalar:
- Toplu işlem araçları küçük ekranda ana filtreleri sıkıştırmamalıdır.
- Gereksiz horizontal scroll ana kullanım akışına bağımlı hale gelmemelidir.
