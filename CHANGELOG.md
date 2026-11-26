# Sürüm Geçmişi (Changelog)

Bu dosya CleanSweep projesinin her sürümünde yapılan değişiklikleri ve neden güncellendiğini belgelemek için tutulur. GitHub'da oluşturulan her yeni Release notu bu dosyadaki ilgili sürüm başlığından otomatik alınır.

## [0.4.0] - 2026-11-26

### Neden Güncellendi?
Kullanıcı geri bildirimleri doğrultusunda Windows Mağaza/sistem uygulamalarının tamamının listelenmesi, tüm başlangıç kaynaklarının (Windows uygulamaları ve zamanlanmış görevler dahil) görünmesi ve Windows Araçları bölümünün kapsamlı hale getirilmesi sağlandı.

### Yeni Özellikler
- **Tüm Windows Uygulamaları (Store & Sistem Uygulamaları)**: `AppxProvider` filtresi genişletildi (`IsFramework` ve `IsResourcePackage` dışındaki tüm ~93 Windows/Store uygulaması artık listeleniyor) ve `AppxManifest.xml` üzerinden okunabilir adlar çözümlendi.
- **Her Modda Görünür Kaynak Filtreleri**: `Program Kaldır` sekmesindeki `Tümü`, `Programlar (Win32)`, `Windows Uygulamaları (Store)`, `Scoop` ve `Chocolatey` filtre butonları Kolay Mod'da da görünür hale getirildi.
- **Kapsamlı Açılış Programları (Autorun) Taraması**: Mevcut `Run` ve `Başlangıç Klasörü` öğelerine ek olarak `RunOnce`, `Policies\Explorer\Run`, **MSIX / Windows Store Başlangıç Görevleri (`AppModel\SystemAppData`)** ve **Oturum Açma / Önyükleme Zamanlanmış Görevleri (`LogonTrigger`/`BootTrigger`)** eklendi; tamamı tek tıkla Aç/Kapa anahtarıyla yönetilebiliyor.
- **30 Windows Sistem Aracı + Anlık Arama**: `Windows Araçları` bölümü 15'ten **30 yerleşik Windows aracına** çıkarıldı (*Disk Yönetimi, Olay Görüntüleyici, Güvenilirlik Geçmişi, Gelişmiş Güvenlik Duvarı, msconfig, dxdiag, Ortam Değişkenleri, Bellek Tanılama, Güç Seçenekleri, CMD, PowerShell* vb.) ve üstüne anlık arama kutusu eklendi.

---

## [0.3.0] - 2026-11-26

### Neden Güncellendi?
Kullanıcı geri bildirimleri doğrultusunda program kaldırma akışı Revo Uninstaller deneyimine yaklaştırıldı, görsel tema hataları düzeltildi ve gereksiz dosya tarayıcısının yalnızca seçili kategorileri taraması sağlandı.

### Yeni Özellikler
- **Program Kaldır Listesinde Kutucuklar & Satır İçi "Kaldır" Butonu**: Her programın soluna seçim kutucuğu (`Seç`), sağına ise doğrudan tek tıkla **`Kaldır`** butonu eklendi.
- **Kolay Mod'da da Aktif "Seçilenleri Toplu Kaldır"**: Birden fazla programı işaretleyip tek seferde sırayla kaldırma ve tüm artık dosya + kayıt defteri anahtarlarını birleştirip temizleme özelliği Kolay Mod'a taşındı.
- **Açılış Programlarında Tıklanabilir Aç/Kapa Anahtarı**: `Açılış Programları` sekmesine modern Windows 11 tarzı **Aç/Kapa anahtarı (`ModernSwitchToggle`)** ve satıra çift tıklayarak anında durum değiştirme eklendi.
- **Sürüm Gösterimi & GitHub Release Otomasyonu**: Başlık çubuğunda ve CLI `--version` çıktısında aktif sürüm (`v0.3.0`) gösterimi, otomatik sürüm notlu GitHub Release workflow'u (`.github/workflows/release.yml`) ve gerçek ekran görüntülü `README.md` dokümantasyonu eklendi.

### Hata Düzeltmeleri
- **Sadece Seçili Kategorileri Tarama**: `Bilgisayarı Temizle` sekmesinde artık işaretsiz kategoriler taranmıyor; kullanıcı seçimleri sıfırlanmıyor ve toplam boyut özeti kutucuklar değiştikçe anlık güncelleniyor.
- **Koyu Tema Arka Plan Düzeltmesi**: `MainWindow`, `LeftoverReviewWindow` ve `ForcedUninstallWindow` pencerelerindeki beyaz arka plan hatası giderildi.
- **MSIX `WindowsApps` Koruması & Kullanıcı Verisi Taraması**: `C:\Program Files\WindowsApps\...` klasörünün `TrustedInstaller` kilidi nedeniyle oluşan `"0 öğe silindi, 1 hata"` sorunu düzeltildi; MSIX paketleri doğrudan `Remove-AppxPackage` ile kaldırılıyor ve `%LOCALAPPDATA%\Packages\<PackageFamilyName>` içindeki gerçek kullanıcı artıkları taranıyor.
- **Avcı Modu Z-Sırası Algılama & Sütun Taşma Düzeltmeleri**: Avcı Modu nişangahının kendi penceresini yakalaması giderildi; tablo metinlerine taşma kısaltması (`CharacterEllipsis`) ve temizlik hatalarına ayrıntılı hata mesajı eklendi; kalıntı silme arka plan iş parçacığına alındı.

---

## [0.2.0] - 2026-11-26

### Neden Güncellendi?
Sadece program kaldırma aracı olmaktan çıkıp Revo Uninstaller Pro'daki tüm temel bakım araçlarını kapsayan, teknik bilgisi olmayan kullanıcıların da güvenle kullanabileceği kapsamlı bir temizlik paketine dönüştürüldü.

### Yeni Özellikler
- **Özel Çoklu-Çözünürlüklü Uygulama İkonu**: `16x16`–`256x256` özel `cleansweep.ico` ikonu hem GUI hem CLI yürütücülerine gömüldü.
- **Kolay Mod / Uzman Modu & Yeni Ana Sayfa**: Çocukların ve yeni başlayanların bile güvenle kullanabilmesi için 4 dev butonlu **Ana Sayfa** ve tek tıkla geçiş yapılan **Kolay Mod / Uzman Modu** eklendi.
- **Avcı Modu (Hunter Mode)**: Yüzen nişangah ile ekrandaki herhangi bir pencere veya kısayol üzerinden program kaldırma/kapatma.
- **Zorla Kaldır & 3 Seviyeli Tarama**: Normal yollarla kaldırılamayan programlar için bozuk MSI/Uninstall kayıtlarını, kilitli süreçleri ve `ReadOnly/Hidden/System` klasörleri temizleyen Zorla Kaldırma motoru.
- **Tarayıcı Eklentileri, Başlangıç Yöneticisi, Gereksiz Dosya Temizleyici, Kalıcı Dosya Öğütücü (DoD)** ve **15 Windows Sistem Aracı**.

---

## [0.1.0] - 2026-11-26

### İlk Sürüm
- Sıfır dış bağımlılıklı `.NET 10` çekirdek motor (`CleanSweep.Core`), masaüstü uygulaması (`CleanSweep.App`) ve CLI (`cleansweep`).
- Win32/MSI, MSIX/Store, Scoop ve Chocolatey uygulama keşfi.
- 7 yüzeyli açıklanabilir kalıntı tarama motoru, `SafetyGuard` koruma duvarı ve 0–100 güven puanlaması.
- ZIP + tipli Registry JSON + `.reg` tabanlı **Atomik Karantina & Geri Yükleme**.
- Kernel sürücüsüz (`revoflt.sys` olmadan) snapshot + `FileSystemWatcher` tabanlı **Kurulum İzleyici**.
- Açık kaynak JSON topluluk kuralları (`rules/*.json`).
