# NetworkHealthMonitor Kullanıcı Kılavuzu

Bu kılavuz, uygulamayı günlük kullanımda nasıl yöneteceğinizi açıklar. Teknik terimler kısa notlarla açıklanmıştır.

## Uygulamayı Açma

Uygulamayı çalıştırdığınızda ana ekranda dashboard, filtreler, cihaz listesi ve seçili cihaz detay paneli görünür. İlk açılışta veritabanı ve ayarlar kullanıcı profilinde otomatik hazırlanır.

## Cihaz Ekleme

1. Cihazlar ekranında **Yeni Cihaz** düğmesine basın.
2. Cihaz adı, IP adresi ve cihaz tipini girin.
3. İsterseniz grup, lokasyon, açıklama ve özel policy alanlarını doldurun.
4. **Kaydet** düğmesine basın.

IP adresi geçersizse veya aynı IP zaten kayıtlıysa uygulama kayıt işlemini durdurur ve uyarı gösterir.

## Cihaz Düzenleme

Cihaz satırındaki **Düzenle** düğmesini veya detay panelindeki **Düzenle** düğmesini kullanın. Değişiklikleri kaydettiğinizde cihaz listesi ve detay paneli yenilenir.

## Cihazları Filtreleme

Cihaz listesinde ad, IP, lokasyon, grup veya açıklama arayabilirsiniz. Ayrıca tip, durum, grup ve kritik cihaz filtreleri kullanılabilir.

## Tek Cihaz Ping

Cihaz satırındaki **Ping** düğmesi yalnız o cihaza manuel ping atar. Sonuç cihaz durumuna, gecikme alanına ve ping loglarına yansır.

## Seçili Cihazlara Ping

Birden fazla satır seçip **Seçilileri Ping** komutunu kullanabilirsiniz. Bu işlem yalnız seçili cihazlara uygulanır.

## Grup, Tip ve Tüm Cihaz Ping

- Grup seçiliyse grup ping komutu o gruptaki cihazları kontrol eder.
- Tip filtresi seçiliyken **Tipe Ping** komutu o tipteki cihazları kontrol eder.
- Tüm cihaz ping komutu listedeki cihazları merkezi paralellik sınırına göre kontrol eder.

## Otomatik Kontrol Ayarları

Otomatik kontrol planları belirli aralıklarla cihazları kontrol eder. Pasif planlar çalışmaz. Pasif cihazlar ve otomatik kontrolü kapalı cihazlar otomatik akışa dahil edilmez.

Global otomatik kontrol, scheduler kontrol sıklığı ve varsayılan kontrol aralığı **Ayarlar** ekranından yönetilir.

## Akıllı Retry Durumları

Ping yanıtı alınamaması cihazın kesin kapalı olduğu anlamına gelmez. Firewall, ICMP kapatma veya ağ politikası etkili olabilir.

- **Sağlıklı**: Son kontrolde ping yanıtı alındı.
- **Uyarı**: İlk veya erken başarısız ölçüm var; cihaz takip edilmeye başlanır.
- **Takipte**: Kısa aralıklı retry denemeleri devam eder.
- **Muhtemel erişilemiyor**: Tekrarlı başarısız ölçümlerden sonra cihaz erişilemiyor olabilir.
- **Ping yanıtlamıyor olabilir**: Cihaz açık olsa bile ping'e cevap vermiyor olabilir.
- **Kontrol edilmedi**: Henüz ölçüm yapılmadı.

Başarılı ping geldiğinde ardışık başarısızlık sayacı sıfırlanır.

## Policy Kaynağı Ne Demek?

Policy, cihazın hangi aralıkla ve hangi timeout/retry değerleriyle kontrol edileceğini belirleyen ayardır. Öncelik sırası:

1. Cihaz özel ayarı
2. Grup varsayılanı
3. Tip varsayılanı
4. Global ayar

Cihaz detay panelinde etkin policy kaynağı ve kullanılan değerler gösterilir.

## CSV ile Cihaz Import

1. **CSV Şablonu** ile örnek dosya oluşturun.
2. Cihazları şablondaki kolonlara göre doldurun.
3. **CSV Import** komutunu çalıştırın.
4. Duplicate IP varsa uygulama güncelleme, atlama veya iptal seçimi sunar.

Geçersiz IP, eksik cihaz adı veya bilinmeyen tip olan satırlar import raporunda hatalı olarak gösterilir.

## CSV ile Cihaz Export

**CSV Export** komutu cihaz listesini Excel uyumlu CSV olarak dışa aktarır. Türkçe karakterler için UTF-8 BOM kullanılır.

## Uptime CSV Dışa Aktarma

Uptime ekranında **Uptime CSV Dışa Aktar** komutu tüm cihazların 24 saat, 7 gün, 30 gün ve genel uptime değerlerini dışa aktarır. Dosya adı tarih/saat içerir.

## Toplu İşlemler

Cihaz listesinde birden fazla satır seçerek şu işlemleri yapabilirsiniz:

- Seçili cihazlara ping
- Otomatik kontrolü aç/kapat
- Gruba ata
- Özel kontrol aralığı uygula
- Pasifleştir

Veriyi değiştiren toplu işlemler kaç cihazın etkileneceğini gösterir ve onay ister.

## Eski Logları Temizleme

**Ayarlar** veya log ekranındaki eski log temizleme komutu, retention süresinden eski ping loglarını siler. Cihaz kayıtları silinmez.

## Veritabanını Optimize Etme

**Veritabanını Optimize Et** komutu SQLite bakım işlemi çalıştırır. Büyük log temizliği sonrası kullanılması önerilir.

## Ayarlar Ekranı

Ayarlar şu gruplara ayrılmıştır:

- **Ping Ayarları**: Timeout ve maksimum paralel ping.
- **Otomatik Kontrol Ayarları**: Global kontrol aralığı, scheduler sıklığı ve başlangıç davranışı.
- **Retry / Akıllı Kontrol Ayarları**: Hızlı retry aralığı, retry limiti ve başarısızlık eşiği.
- **Tip Bazlı Politikalar**: Cihaz tipine göre varsayılan policy değerleri.
- **Log ve Uptime Ayarları**: Log saklama süresi.
- **Dışa Aktarma Ayarları**: CSV klasörü ve ayırıcı.
- **Bakım**: Yedekleme, geri yükleme, optimize ve ayar import/export işlemleri.
