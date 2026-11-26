using System.Runtime.InteropServices;
using CleanSweep.Core.Models;

namespace CleanSweep.Core.Cleaner;

public sealed class JunkCleanerService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBinW(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBinW(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    private const uint SherbNoConfirmation = 0x00000001;
    private const uint SherbNoProgressUi = 0x00000002;
    private const uint SherbNoSound = 0x00000004;

    public static List<JunkCategory> BuildCategories()
    {
        string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string progData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string userProf = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var browserCaches = new List<string>();
        string[] chromiumRoots =
        [
            Path.Combine(localApp, "Google", "Chrome", "User Data"),
            Path.Combine(localApp, "Microsoft", "Edge", "User Data"),
            Path.Combine(localApp, "BraveSoftware", "Brave-Browser", "User Data")
        ];

        foreach (string root in chromiumRoots)
        {
            if (!Directory.Exists(root)) continue;
            string[] profiles = [Path.Combine(root, "Default")];
            try
            {
                profiles = profiles.Concat(Directory.EnumerateDirectories(root, "Profile *")).ToArray();
            }
            catch { }

            foreach (string prof in profiles)
            {
                browserCaches.Add(Path.Combine(prof, "Cache", "Cache_Data"));
                browserCaches.Add(Path.Combine(prof, "Code Cache"));
                browserCaches.Add(Path.Combine(prof, "GPUCache"));
            }
        }

        string ffProfiles = Path.Combine(localApp, "Mozilla", "Firefox", "Profiles");
        if (Directory.Exists(ffProfiles))
        {
            try
            {
                foreach (string p in Directory.EnumerateDirectories(ffProfiles))
                    browserCaches.Add(Path.Combine(p, "cache2"));
            }
            catch { }
        }

        return
        [
            new JunkCategory
            {
                Id = "user-temp",
                Name = "Kullanıcı Geçici Dosyaları (%TEMP%)",
                Description = "Kurulum ve uygulamaların bıraktığı geçici dosyalar.",
                Group = "Sistem",
                DefaultSelected = true,
                IsSelected = true,
                TargetPaths = [Path.GetTempPath()]
            },
            new JunkCategory
            {
                Id = "win-temp",
                Name = "Windows Geçici Dosyaları",
                Description = "Sistem genelindeki geçici çalışma dosyaları (C:\\Windows\\Temp).",
                Group = "Sistem",
                DefaultSelected = true,
                IsSelected = true,
                TargetPaths = [Path.Combine(winDir, "Temp")]
            },
            new JunkCategory
            {
                Id = "recycle-bin",
                Name = "Geri Dönüşüm Kutusu",
                Description = "Geri Dönüşüm Kutusu'nda bekleyen silinmiş dosyalar.",
                Group = "Sistem",
                DefaultSelected = true,
                IsSelected = true,
                IsRecycleBin = true
            },
            new JunkCategory
            {
                Id = "browser-cache",
                Name = "Tarayıcı Önbellekleri",
                Description = "Chrome, Edge, Brave ve Firefox önbellek dosyaları (parola ve yer imlerine dokunmaz).",
                Group = "Tarayıcı",
                DefaultSelected = false,
                IsSelected = false,
                TargetPaths = browserCaches.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            },
            new JunkCategory
            {
                Id = "thumb-cache",
                Name = "Küçük Resim (Thumbnail) Önbelleği",
                Description = "Windows Gezgini tarafından önbelleğe alınan simge ve önizleme veritabanları.",
                Group = "Sistem",
                DefaultSelected = false,
                IsSelected = false,
                TargetPaths = [Path.Combine(localApp, "Microsoft", "Windows", "Explorer")],
                FilePattern = "thumbcache_*.db"
            },
            new JunkCategory
            {
                Id = "crash-dumps",
                Name = "Hata Dökümleri ve Raporlar",
                Description = "Uygulama çökme dökümleri (CrashDumps) ve Windows Hata Raporları (WER).",
                Group = "Sistem",
                DefaultSelected = true,
                IsSelected = true,
                TargetPaths =
                [
                    Path.Combine(localApp, "CrashDumps"),
                    Path.Combine(progData, "Microsoft", "Windows", "WER", "ReportArchive")
                ]
            },
            new JunkCategory
            {
                Id = "recent-docs",
                Name = "Son Kullanılan Belgeler Geçmişi",
                Description = "Windows Son Kullanılanlar kısayol listesi (gizlilik temizliği).",
                Group = "Gizlilik",
                DefaultSelected = false,
                IsSelected = false,
                TargetPaths = [Path.Combine(appData, "Microsoft", "Windows", "Recent")]
            },
            new JunkCategory
            {
                Id = "dev-caches",
                Name = "Geliştirici Paket Önbellekleri",
                Description = "npm, pip ve NuGet yerel paket indirme önbellekleri.",
                Group = "Geliştirici",
                DefaultSelected = false,
                IsSelected = false,
                TargetPaths =
                [
                    Path.Combine(localApp, "npm-cache"),
                    Path.Combine(localApp, "pip", "cache"),
                    Path.Combine(userProf, ".nuget", "v3-cache")
                ]
            }
        ];
    }

    public async Task<List<JunkCategory>> ScanAllAsync(CancellationToken ct = default)
    {
        var categories = BuildCategories();
        await ScanCategoriesAsync(categories, onlySelected: false, ct);
        return categories;
    }

    public Task ScanCategoriesAsync(
        IReadOnlyList<JunkCategory> categories,
        bool onlySelected = true,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            Parallel.ForEach(categories, new ParallelOptions { CancellationToken = ct }, cat =>
            {
                if (onlySelected && !cat.IsSelected)
                {
                    cat.FileCount = 0;
                    cat.TotalBytes = 0;
                    return;
                }

                if (cat.IsRecycleBin)
                {
                    var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
                    if (SHQueryRecycleBinW(null, ref info) == 0)
                    {
                        cat.TotalBytes = Math.Max(0, info.i64Size);
                        cat.FileCount = (int)Math.Max(0, info.i64NumItems);
                    }
                    else
                    {
                        cat.TotalBytes = 0;
                        cat.FileCount = 0;
                    }
                    return;
                }

                int count = 0;
                long bytes = 0;
                string pattern = cat.FilePattern ?? "*";

                foreach (string dir in cat.TargetPaths)
                {
                    if (!Directory.Exists(dir)) continue;
                    var (c, b) = MeasureDirectory(dir, pattern, recursive: cat.FilePattern == null, ct);
                    count += c;
                    bytes += b;
                }

                cat.FileCount = count;
                cat.TotalBytes = bytes;
            });
        }, ct);
    }

    public Task<JunkCleanSummary> CleanSelectedAsync(IEnumerable<JunkCategory> categories, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var summary = new JunkCleanSummary();
            foreach (var cat in categories.Where(c => c.IsSelected))
            {
                ct.ThrowIfCancellationRequested();
                if (cat.IsRecycleBin)
                {
                    if (cat.FileCount > 0)
                    {
                        int hr = SHEmptyRecycleBinW(IntPtr.Zero, null, SherbNoConfirmation | SherbNoProgressUi | SherbNoSound);
                        if (hr == 0)
                        {
                            summary.DeletedFiles += cat.FileCount;
                            summary.FreedBytes += cat.TotalBytes;
                        }
                    }
                    continue;
                }

                string pattern = cat.FilePattern ?? "*";
                bool recursive = cat.FilePattern == null;
                foreach (string dir in cat.TargetPaths)
                {
                    if (!Directory.Exists(dir)) continue;
                    CleanDirectoryContentsSafe(dir, pattern, recursive, summary, ct);
                }
            }
            return summary;
        }, ct);
    }

    public static void CleanDirectoryContentsSafe(
        string directory,
        string pattern,
        bool recursive,
        JunkCleanSummary summary,
        CancellationToken ct = default)
    {
        try
        {
            var opt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            foreach (string file in Directory.EnumerateFiles(directory, pattern, opt))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var fi = new FileInfo(file);
                    long len = fi.Length;
                    fi.Attributes = FileAttributes.Normal;
                    fi.Delete();
                    summary.DeletedFiles++;
                    summary.FreedBytes += len;
                }
                catch
                {
                    summary.SkippedLockedFiles++;
                }
            }

            if (recursive)
            {
                foreach (string sub in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories)
                    .OrderByDescending(d => d.Length))
                {
                    try
                    {
                        if (!Directory.EnumerateFileSystemEntries(sub).Any())
                            Directory.Delete(sub);
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            summary.Errors.Add($"{directory}: {ex.Message}");
        }
    }

    private static (int Count, long Bytes) MeasureDirectory(string dir, string pattern, bool recursive, CancellationToken ct)
    {
        int count = 0;
        long bytes = 0;
        try
        {
            var opt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            foreach (string file in Directory.EnumerateFiles(dir, pattern, opt))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    bytes += new FileInfo(file).Length;
                    count++;
                }
                catch { }
            }
        }
        catch { }
        return (count, bytes);
    }
}
