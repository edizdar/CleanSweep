# Güvenlik Politikası

CleanSweep, sistem dosyalarını ve Windows Kayıt Defteri'ni değiştirebilen bir araçtır. Bu nedenle çok katmanlı güvenlik önlemleri içerir:

1. **SafetyGuard Koruma Listesi**: Sistem kökleri (`C:\Windows`, `C:\Program Files`, `C:\Users`, `HKLM\SOFTWARE\Microsoft`, `Run` kök anahtarı vb.) silme işlemine karşı kod seviyesinde engellenmiştir.
2. **Yayıncı Çakışma Koruması**: Aynı yayıncıya ait başka uygulamalar kuruluysa ortak yayıncı klasörleri otomatik olarak `Caution` seviyesine düşürülür ve varsayılan olarak seçilmez.
3. **Varsayılan Karantina**: Silinen dosya ve kayıt defteri anahtarları silmeden önce `%LocalAppData%\CleanSweep\Quarantine\` altına ZIP + JSON + `.reg` olarak yedeklenir.
4. **En Az Yetki İlkesi**: Uygulama `asInvoker` olarak başlar; yalnızca gerektiğinde kullanıcı onayıyla yönetici yükseltmesi ister.

Bir güvenlik açığı tespit ederseniz lütfen herkese açık issue açmadan önce proje yöneticileriyle iletişime geçin.
