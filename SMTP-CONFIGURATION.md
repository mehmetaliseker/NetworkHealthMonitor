# SMTP Configuration

SMTP ayarlari UI icindeki `Ayarlar > E-posta / SMTP` ekranindan yapilir.

Gerekli alanlar:

- SMTP sunucu adresi
- Port
- Guvenlik modu: STARTTLS, SSL/TLS veya guvenliksiz
- Kullanici adi
- Parola
- Gonderen e-posta adresi
- Gonderen adi
- Test e-postasi alicisi

SMTP parolasi kaynak koda, release paketine veya loglara yazilmaz. Kayıtli parola UI'da maskeli gosterilir. Parola degistirilmeden ayar kaydedilirse mevcut secret korunur.

Once `Baglantiyi test et`, sonra `Test e-postasi gonder` islemini kullanin.
