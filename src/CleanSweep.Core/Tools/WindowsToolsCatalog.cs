using System.Diagnostics;

namespace CleanSweep.Core.Tools;

public sealed class WindowsToolItem
{
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
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
            Command = "rstrui.exe",
            IconGlyph = "\uE81C"
        },
        new()
        {
            Title = "Görev Yöneticisi",
            Description = "Donan veya çalışan programları görün ve kapatın.",
            Command = "taskmgr.exe",
            IconGlyph = "\uE9D9"
        },
        new()
        {
            Title = "Disk Temizleme",
            Description = "Eski Windows Update dosyalarını ve sistem çöplerini silin.",
            Command = "cleanmgr.exe",
            IconGlyph = "\uE74D"
        },
        new()
        {
            Title = "Sürücüleri İyileştir",
            Description = "SSD ve sabit disklerinizi optimize edip hızlandırın.",
            Command = "dfrgui.exe",
            IconGlyph = "\uEDA2"
        },
        new()
        {
            Title = "Akıllı Depolama (Win11)",
            Description = "Windows'un otomatik gereksiz dosya temizleme ayarını açın.",
            Command = "explorer.exe",
            Arguments = "ms-settings:storagesense",
            IconGlyph = "\uE7F8"
        },
        new()
        {
            Title = "Windows Güncelleştirme",
            Description = "Sistem güncellemelerini ve güvenlik yamalarını kontrol edin.",
            Command = "explorer.exe",
            Arguments = "ms-settings:windowsupdate",
            IconGlyph = "\uE895"
        },
        new()
        {
            Title = "Windows Güvenliği",
            Description = "Virüs taraması yapın ve güvenlik duvarını kontrol edin.",
            Command = "explorer.exe",
            Arguments = "windowsdefender:",
            IconGlyph = "\uEA18"
        },
        new()
        {
            Title = "Windows Hizmetleri",
            Description = "Arka planda çalışan sistem servislerini başlatın veya durdurun.",
            Command = "services.msc",
            IconGlyph = "\uE90F"
        },
        new()
        {
            Title = "Görev Zamanlayıcı",
            Description = "Belirli saatlerde otomatik çalışan görevleri kontrol edin.",
            Command = "taskschd.msc",
            IconGlyph = "\uE823"
        },
        new()
        {
            Title = "Kaynak İzleyici",
            Description = "İşlemci, RAM, disk ve interneti kimin kullandığını görün.",
            Command = "resmon.exe",
            IconGlyph = "\uE9D2"
        },
        new()
        {
            Title = "Aygıt Yöneticisi",
            Description = "Ekran kartı, ses ve USB donanım sürücülerini yönetin.",
            Command = "devmgmt.msc",
            IconGlyph = "\uE7F4"
        },
        new()
        {
            Title = "Kayıt Defteri Düzenleyici",
            Description = "Windows Kayıt Defteri (Registry) ayarlarını inceleyin.",
            Command = "regedit.exe",
            IconGlyph = "\uE713",
            RequiresAdmin = true
        },
        new()
        {
            Title = "Sistem Bilgisi",
            Description = "Bilgisayar donanımı, RAM ve Windows sürümünü görüntüleyin.",
            Command = "msinfo32.exe",
            IconGlyph = "\uE946"
        },
        new()
        {
            Title = "Ağ Bağlantıları",
            Description = "Wi-Fi, Ethernet ve VPN ağ ayarlarını yönetin.",
            Command = "ncpa.cpl",
            IconGlyph = "\uE774"
        },
        new()
        {
            Title = "Klasik Program Kaldır",
            Description = "Windows Denetim Masası klasik program kaldırma listesini açın.",
            Command = "control.exe",
            Arguments = "appwiz.cpl",
            IconGlyph = "\uE71D"
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
