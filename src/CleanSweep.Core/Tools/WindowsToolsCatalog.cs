using System.Diagnostics;

namespace CleanSweep.Core.Tools;

public sealed class WindowsToolItem
{
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Category { get; init; } = "Sistem";
    public string Command { get; init; } = string.Empty;
    public string Arguments { get; init; } = string.Empty;
    public string IconGlyph { get; init; } = "\uE713";
    public bool RequiresAdmin { get; init; }
}

public static class WindowsToolsCatalog
{
    public static IReadOnlyList<WindowsToolItem> GetAll() =>
    [
        new()
        {
            Title = "Sistem Geri Yükleme",
            Description = "Bilgisayarı sorunsuz çalıştığı önceki bir tarihe geri döndürün.",
            Category = "Kurtarma",
            Command = "rstrui.exe",
            IconGlyph = "\uE81C"
        },
        new()
        {
            Title = "Görev Yöneticisi",
            Description = "Donan veya çalışan programları görün ve kapatın.",
            Category = "İzleme",
            Command = "taskmgr.exe",
            IconGlyph = "\uE9D9"
        },
        new()
        {
            Title = "Disk Temizleme",
            Description = "Eski Windows Update dosyalarını ve sistem çöplerini silin.",
            Category = "Disk",
            Command = "cleanmgr.exe",
            IconGlyph = "\uE74D"
        },
        new()
        {
            Title = "Disk Yönetimi",
            Description = "Disk bölümlerini oluşturun, küçültün veya harf atayın.",
            Category = "Disk",
            Command = "diskmgmt.msc",
            IconGlyph = "\uEDA2"
        },
        new()
        {
            Title = "Sürücüleri İyileştir",
            Description = "SSD ve sabit disklerinizi optimize edip hızlandırın.",
            Category = "Disk",
            Command = "dfrgui.exe",
            IconGlyph = "\uE9F5"
        },
        new()
        {
            Title = "Akıllı Depolama (Win11)",
            Description = "Windows'un otomatik gereksiz dosya temizleme ayarını açın.",
            Category = "Disk",
            Command = "explorer.exe",
            Arguments = "ms-settings:storagesense",
            IconGlyph = "\uE7F8"
        },
        new()
        {
            Title = "Windows Güncelleştirme",
            Description = "Sistem güncellemelerini ve güvenlik yamalarını kontrol edin.",
            Category = "Sistem",
            Command = "explorer.exe",
            Arguments = "ms-settings:windowsupdate",
            IconGlyph = "\uE895"
        },
        new()
        {
            Title = "Windows Güvenliği",
            Description = "Virüs taraması yapın ve güvenlik duvarını kontrol edin.",
            Category = "Güvenlik",
            Command = "explorer.exe",
            Arguments = "windowsdefender:",
            IconGlyph = "\uEA18"
        },
        new()
        {
            Title = "Gelişmiş Güvenlik Duvarı",
            Description = "Gelen ve giden ağ kurallarını ayrıntılı olarak yönetin.",
            Category = "Güvenlik",
            Command = "wf.msc",
            IconGlyph = "\uE83D"
        },
        new()
        {
            Title = "Windows Hizmetleri",
            Description = "Arka planda çalışan sistem servislerini başlatın veya durdurun.",
            Category = "Sistem",
            Command = "services.msc",
            IconGlyph = "\uE90F"
        },
        new()
        {
            Title = "Görev Zamanlayıcı",
            Description = "Belirli saatlerde otomatik çalışan görevleri kontrol edin.",
            Category = "Sistem",
            Command = "taskschd.msc",
            IconGlyph = "\uE823"
        },
        new()
        {
            Title = "Kaynak İzleyici",
            Description = "İşlemci, RAM, disk ve interneti kimin kullandığını görün.",
            Category = "İzleme",
            Command = "resmon.exe",
            IconGlyph = "\uE9D2"
        },
        new()
        {
            Title = "Performans İzleyicisi",
            Description = "Sistem performans sayaçlarını ve günlüklerini analiz edin.",
            Category = "İzleme",
            Command = "perfmon.msc",
            IconGlyph = "\uE9D9"
        },
        new()
        {
            Title = "Güvenilirlik Geçmişi",
            Description = "Uygulama çökmelerini ve sistem hatalarını zaman çizelgesinde görün.",
            Category = "Teşhis",
            Command = "perfmon.exe",
            Arguments = "/rel",
            IconGlyph = "\uE7BA"
        },
        new()
        {
            Title = "Olay Görüntüleyici",
            Description = "Windows sistem, uygulama ve güvenlik kayıtlarını inceleyin.",
            Category = "Teşhis",
            Command = "eventvwr.msc",
            IconGlyph = "\uE7C4"
        },
        new()
        {
            Title = "Aygıt Yöneticisi",
            Description = "Ekran kartı, ses ve USB donanım sürücülerini yönetin.",
            Category = "Donanım",
            Command = "devmgmt.msc",
            IconGlyph = "\uE7F4"
        },
        new()
        {
            Title = "DirectX Tanı Aracı",
            Description = "Ekran kartı, ses ve DirectX sürücü bilgilerini test edin.",
            Category = "Donanım",
            Command = "dxdiag.exe",
            IconGlyph = "\uE7FC"
        },
        new()
        {
            Title = "Windows Bellek Tanılama",
            Description = "RAM bellekte donanımsal hata olup olmadığını kontrol edin.",
            Category = "Donanım",
            Command = "mdsched.exe",
            IconGlyph = "\uE950"
        },
        new()
        {
            Title = "Sistem Yapılandırması",
            Description = "Güvenli mod, önyükleme süresi ve başlatma seçenekleri (msconfig).",
            Category = "Sistem",
            Command = "msconfig.exe",
            IconGlyph = "\uE713"
        },
        new()
        {
            Title = "Kayıt Defteri Düzenleyici",
            Description = "Windows Kayıt Defteri (Registry) ayarlarını inceleyin.",
            Category = "Sistem",
            Command = "regedit.exe",
            IconGlyph = "\uE8F1",
            RequiresAdmin = true
        },
        new()
        {
            Title = "Sistem Bilgisi",
            Description = "Bilgisayar donanımı, BIOS, RAM ve Windows sürümünü görün.",
            Category = "Teşhis",
            Command = "msinfo32.exe",
            IconGlyph = "\uE946"
        },
        new()
        {
            Title = "Sistem Özellikleri",
            Description = "Bilgisayar adı, sanal bellek ve sistem koruma ayarları.",
            Category = "Sistem",
            Command = "sysdm.cpl",
            IconGlyph = "\uE770"
        },
        new()
        {
            Title = "Ortam Değişkenleri (PATH)",
            Description = "Kullanıcı ve sistem PATH ortam değişkenlerini düzenleyin.",
            Category = "Geliştirici",
            Command = "rundll32.exe",
            Arguments = "sysdm.cpl,EditEnvironmentVariables",
            IconGlyph = "\uE943"
        },
        new()
        {
            Title = "Güç Seçenekleri",
            Description = "Yüksek performans veya dengeli güç planlarını yapılandırın.",
            Category = "Donanım",
            Command = "powercfg.cpl",
            IconGlyph = "\uE83F"
        },
        new()
        {
            Title = "Ağ Bağlantıları",
            Description = "Wi-Fi, Ethernet, DNS ve VPN ağ ayarlarını yönetin.",
            Category = "Ağ",
            Command = "ncpa.cpl",
            IconGlyph = "\uE774"
        },
        new()
        {
            Title = "Bilgisayar Yönetimi",
            Description = "Yerel kullanıcılar, paylaşılan klasörler ve disk araçları.",
            Category = "Sistem",
            Command = "compmgmt.msc",
            IconGlyph = "\uE7EF"
        },
        new()
        {
            Title = "Uygulamalar (Win11 Ayarlar)",
            Description = "Modern Windows Ayarlar > Yüklü Uygulamalar sayfasını açın.",
            Category = "Uygulamalar",
            Command = "explorer.exe",
            Arguments = "ms-settings:appsfeatures",
            IconGlyph = "\uE71D"
        },
        new()
        {
            Title = "Klasik Program Kaldır",
            Description = "Windows Denetim Masası klasik program kaldırma listesini açın.",
            Category = "Uygulamalar",
            Command = "control.exe",
            Arguments = "appwiz.cpl",
            IconGlyph = "\uE74E"
        },
        new()
        {
            Title = "Komut İstemi (CMD)",
            Description = "Yönetim ve bakım komutları için klasik komut satırını açın.",
            Category = "Geliştirici",
            Command = "cmd.exe",
            IconGlyph = "\uE756"
        },
        new()
        {
            Title = "Windows PowerShell",
            Description = "Gelişmiş sistem otomasyonu için PowerShell penceresini açın.",
            Category = "Geliştirici",
            Command = "powershell.exe",
            IconGlyph = "\uE756"
        }
    ];

    public static void Launch(WindowsToolItem tool)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = tool.Command,
            Arguments = tool.Arguments,
            UseShellExecute = true
        });
    }
}
