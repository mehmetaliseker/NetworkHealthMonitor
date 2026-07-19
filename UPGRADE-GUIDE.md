# Upgrade Guide

1. Yonetici PowerShell acin.
2. Mevcut data ve ayar yedegini alin.
3. UI aciksa kapatin.
4. `scripts\upgrade-service.ps1 -NewWorkerPath <yeni worker klasoru veya exe>` komutunu calistirin.
5. Script Worker'i durdurur, backup alir, yeni binary'leri kopyalar, `--run-once` ile migration'i tetikler ve servisi baslatir.
6. `scripts\production-readiness-test.ps1` ile durumu dogrulayin.

Korunmasi gereken veriler: cihazlar, gruplar, planlar, ping loglari, uptime, SMTP/ntfy ayarlari, alici listeleri, sablonlar, acik incident'lar, outbox ve susturma kayitlari.
