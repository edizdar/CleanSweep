using CleanSweep.Core.Discovery;
using CleanSweep.Core.Models;
using CleanSweep.Core.Quarantine;
using CleanSweep.Core.Safety;
using CleanSweep.Core.Uninstall;
using Microsoft.Win32;

namespace CleanSweep.Core.Cleaner;

public sealed class RegistryCleanerService
{
    private static readonly HashSet<string> ProtectedVendorSubKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft",
        "Classes",
        "Policies",
        "Clients",
        "ODBC",
        "AppDataLow",
        "Wow6432Node",
        "RegisteredApplications",
        "Windows",
        "Intel",
        "NVIDIA Corporation",
        "AMD",
        "Realtek",
        "CleanSweep"
    };

    private static readonly string[] IgnoredUninstallRunners =
    [
        "msiexec",
        "rundll32",
        "regsvr32",
        "cmd.exe",
        "powershell",
        "pwsh",
        "mshta",
        "wscript",
        "cscript"
    ];

    public static List<RegistryCleanCategory> BuildCategories() =>
    [
        new RegistryCleanCategory
        {
            Id = "mui-appcompat",
            Name = "Eski Uygulama Önbelleği (MuiCache & AppCompat)",
            Description = "Artık diskte bulunmayan uygulamalara ait Windows uyumluluk ve ad önbelleği değerleri.",
            DefaultSelected = true,
            IsSelected = true,
            Risk = RiskLevel.Safe
        },
        new RegistryCleanCategory
        {
            Id = "broken-uninstall",
            Name = "Bozuk Kaldırma Kayıtları (Uninstall)",
            Description = "Kurulum klasörü ve kaldırıcı dosyası silinmiş eski Program Kaldır kayıt anahtarları.",
            DefaultSelected = true,
            IsSelected = true,
            Risk = RiskLevel.Moderate
        },
        new RegistryCleanCategory
        {
            Id = "broken-startup",
            Name = "Eksik Başlangıç Kayıtları (Run / RunOnce)",
            Description = "Hedef .exe dosyası silinmiş olduğu halde hâlâ başlangıçta çalıştırılmaya çalışılan kayıtlar.",
            DefaultSelected = true,
            IsSelected = true,
            Risk = RiskLevel.Safe
        },
        new RegistryCleanCategory
        {
            Id = "broken-apppaths",
            Name = "Kırık Uygulama Kısayolları (App Paths)",
            Description = "Çalıştırılabilir dosyası artık bulunmayan App Paths kayıt defteri anahtarları.",
            DefaultSelected = true,
            IsSelected = true,
            Risk = RiskLevel.Safe
        },
        new RegistryCleanCategory
        {
            Id = "missing-shareddlls",
            Name = "Kayıp Paylaşılan DLL Referansları (SharedDLLs)",
            Description = "Diskten silinmiş paylaşılan .dll/.ocx dosyalarına işaret eden sayaç değerleri.",
            DefaultSelected = true,
            IsSelected = true,
            Risk = RiskLevel.Safe
        },
        new RegistryCleanCategory
        {
            Id = "orphaned-firewall",
            Name = "Yetim Güvenlik Duvarı Kuralları (FirewallRules)",
            Description = "Diskte artık bulunmayan programlara ait kalıntı Windows Güvenlik Duvarı kuralları.",
            DefaultSelected = false,
            IsSelected = false,
            Risk = RiskLevel.Safe
        },
        new RegistryCleanCategory
        {
            Id = "empty-software-keys",
            Name = "Boş Yazılım Kayıt Anahtarları (HKCU\\Software)",
            Description = "İçinde hiç alt anahtar veya değer barındırmayan boş kullanıcı yazılımı anahtarları.",
            DefaultSelected = false,
            IsSelected = false,
            Risk = RiskLevel.Moderate
        }
    ];

    public async Task<List<RegistryIssueItem>> ScanAsync(
        IEnumerable<RegistryCleanCategory> categories,
        bool onlySelected = true,
        CancellationToken ct = default)
    {
        var categoryList = categories.ToList();
        return await Task.Run(() => ScanCategoriesInternal(categoryList, onlySelected, ct), ct);
    }

    public async Task<OperationResult> CleanSelectedAsync(
        IEnumerable<RegistryIssueItem> issues,
        bool backupToQuarantine = true,
        bool dryRun = false,
        QuarantineManager? quarantineManager = null,
        CancellationToken ct = default)
    {
        var selected = issues
            .Where(i => i.IsSelected)
            .Select(i => i.ToLeftoverItem())
            .ToList();

        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var executor = new CleanupExecutor(quarantineManager ?? new QuarantineManager());
            return executor.ExecuteCleanup(
                "Kayıt Defteri Temizliği",
                selected,
                new CleanupOptions
                {
                    BackupToQuarantine = backupToQuarantine,
                    DryRun = dryRun
                });
        }, ct);
    }

    private List<RegistryIssueItem> ScanCategoriesInternal(
        List<RegistryCleanCategory> categories,
        bool onlySelected,
        CancellationToken ct)
    {
        var results = new List<RegistryIssueItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cat in categories)
        {
            ct.ThrowIfCancellationRequested();
            if (onlySelected && !cat.IsSelected)
            {
                cat.FoundCount = 0;
                continue;
            }

            var found = ScanSingleCategory(cat, ct);
            int addedForCat = 0;

            foreach (var issue in found)
            {
                if (!SafetyGuard.IsItemAllowed(issue.ToLeftoverItem()))
                    continue;

                string dedupeKey = $"{issue.ItemType}|{issue.RegistryPath}|{issue.ValueName ?? string.Empty}";
                if (seen.Add(dedupeKey))
                {
                    results.Add(issue);
                    addedForCat++;
                }
            }

            cat.FoundCount = addedForCat;
        }

        return results;
    }

    private List<RegistryIssueItem> ScanSingleCategory(RegistryCleanCategory category, CancellationToken ct)
    {
        return category.Id switch
        {
            "mui-appcompat" => ScanMuiAndAppCompat(category, ct),
            "broken-uninstall" => ScanBrokenUninstall(category, ct),
            "broken-startup" => ScanBrokenStartup(category, ct),
            "broken-apppaths" => ScanBrokenAppPaths(category, ct),
            "missing-shareddlls" => ScanMissingSharedDlls(category, ct),
            "orphaned-firewall" => ScanOrphanedFirewallRules(category, ct),
            "empty-software-keys" => ScanEmptySoftwareKeys(RegistryHive.CurrentUser, @"Software", category, ct),
            _ => []
        };
    }

    private static List<RegistryIssueItem> ScanMuiAndAppCompat(RegistryCleanCategory cat, CancellationToken ct)
    {
        var items = new List<RegistryIssueItem>();

        ScanValuePathNames(
            RegistryHive.CurrentUser,
            @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache",
            "HKCU",
            rawName => ExtractMuiCachePath(rawName),
            cat,
            items,
            ct,
            reasonPrefix: "MuiCache içinde referans verilen dosya diskte bulunamadı");

        ScanValuePathNames(
            RegistryHive.CurrentUser,
            @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Compatibility Assistant\Store",
            "HKCU",
            rawName => rawName,
            cat,
            items,
            ct,
            reasonPrefix: "AppCompat Store önbelleğindeki uygulama diskte bulunamadı");

        ScanValuePathNames(
            RegistryHive.CurrentUser,
            @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers",
            "HKCU",
            rawName => rawName,
            cat,
            items,
            ct,
            reasonPrefix: "Uyumluluk katmanı kaydındaki uygulama diskte bulunamadı");

        return items;
    }

    private static void ScanValuePathNames(
        RegistryHive hive,
        string subKeyPath,
        string hiveLabel,
        Func<string, string?> pathExtractor,
        RegistryCleanCategory cat,
        List<RegistryIssueItem> output,
        CancellationToken ct,
        string reasonPrefix)
    {
        foreach (var view in GetViewsForHive(hive))
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(subKeyPath, writable: false);
                if (key == null) continue;

                string fullPath = $"{hiveLabel}\\{subKeyPath}";
                foreach (string valueName in key.GetValueNames())
                {
                    ct.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(valueName)) continue;
                    if (valueName.Equals("LangID", StringComparison.OrdinalIgnoreCase)) continue;

                    string? candidate = pathExtractor(valueName);
                    if (!IsMissingLocalFile(candidate)) continue;

                    output.Add(new RegistryIssueItem
                    {
                        CategoryId = cat.Id,
                        CategoryName = cat.Name,
                        ItemType = LeftoverCategory.RegistryValue,
                        RegistryPath = fullPath,
                        ValueName = valueName,
                        TargetDescription = candidate!,
                        Reason = $"{reasonPrefix}: {candidate}",
                        Risk = cat.Risk,
                        IsSelected = cat.Risk == RiskLevel.Safe
                    });
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }
    }

    private static List<RegistryIssueItem> ScanBrokenUninstall(RegistryCleanCategory cat, CancellationToken ct)
    {
        var items = new List<RegistryIssueItem>();
        (RegistryHive Hive, string SubKey, string Prefix)[] roots =
        [
            (RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", "HKCU"),
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "HKLM"),
            (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", "HKLM")
        ];

        foreach (var (hive, subKey, prefix) in roots)
        {
            foreach (var view in GetViewsForHive(hive))
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var uninstallRoot = baseKey.OpenSubKey(subKey, writable: false);
                    if (uninstallRoot == null) continue;

                    foreach (string name in uninstallRoot.GetSubKeyNames())
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            using var appKey = uninstallRoot.OpenSubKey(name, writable: false);
                            if (appKey == null) continue;

                            if (appKey.GetValue("SystemComponent") is int sysComp && sysComp == 1)
                                continue;
                            if (appKey.GetValue("ParentKeyName") is string pk && !string.IsNullOrWhiteSpace(pk))
                                continue;

                            string displayName = appKey.GetValue("DisplayName") as string ?? string.Empty;
                            if (string.IsNullOrWhiteSpace(displayName)) continue;

                            string? installLoc = appKey.GetValue("InstallLocation") as string;
                            string? uninstallCmd = (appKey.GetValue("UninstallString") as string)
                                ?? (appKey.GetValue("QuietUninstallString") as string);

                            if (!string.IsNullOrWhiteSpace(uninstallCmd) &&
                                IgnoredUninstallRunners.Any(r => uninstallCmd.Contains(r, StringComparison.OrdinalIgnoreCase)))
                            {
                                continue;
                            }

                            string? uninstallExe = !string.IsNullOrWhiteSpace(uninstallCmd)
                                ? Win32RegistryProvider.ExtractExecutablePath(uninstallCmd)
                                : null;

                            bool hasInstallLoc = !string.IsNullOrWhiteSpace(installLoc);
                            bool hasUninstallExe = !string.IsNullOrWhiteSpace(uninstallExe);

                            if (!hasInstallLoc && !hasUninstallExe)
                                continue;

                            bool missingInstallLoc = hasInstallLoc && IsMissingLocalDirectory(installLoc);
                            bool missingUninstallExe = hasUninstallExe && IsMissingLocalFile(uninstallExe);

                            bool broken = (hasInstallLoc && hasUninstallExe && missingInstallLoc && missingUninstallExe)
                                || (hasUninstallExe && !hasInstallLoc && missingUninstallExe)
                                || (hasInstallLoc && !hasUninstallExe && missingInstallLoc);

                            if (!broken) continue;

                            string missingRef = missingUninstallExe ? uninstallExe! : installLoc!;
                            items.Add(new RegistryIssueItem
                            {
                                CategoryId = cat.Id,
                                CategoryName = cat.Name,
                                ItemType = LeftoverCategory.RegistryKey,
                                RegistryPath = $"{prefix}\\{subKey}\\{name}",
                                ValueName = null,
                                TargetDescription = $"{displayName} ({missingRef})",
                                Reason = $"Programın kurulum/kaldırıcı yolu diskte bulunamıyor: {missingRef}",
                                Risk = cat.Risk,
                                IsSelected = false
                            });
                        }
                        catch (OperationCanceledException) { throw; }
                        catch { }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }
        }

        return items;
    }

    private static List<RegistryIssueItem> ScanBrokenStartup(RegistryCleanCategory cat, CancellationToken ct)
    {
        var items = new List<RegistryIssueItem>();
        (RegistryHive Hive, string SubKey, string Prefix)[] runKeys =
        [
            (RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKCU"),
            (RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", "HKCU"),
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKLM"),
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", "HKLM"),
            (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "HKLM")
        ];

        foreach (var (hive, subKey, prefix) in runKeys)
        {
            foreach (var view in GetViewsForHive(hive))
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var key = baseKey.OpenSubKey(subKey, writable: false);
                    if (key == null) continue;

                    string fullPath = $"{prefix}\\{subKey}";
                    foreach (string valueName in key.GetValueNames())
                    {
                        ct.ThrowIfCancellationRequested();
                        if (string.IsNullOrWhiteSpace(valueName)) continue;

                        if (key.GetValue(valueName) is not string cmd || string.IsNullOrWhiteSpace(cmd))
                            continue;

                        if (IgnoredUninstallRunners.Any(r => cmd.Contains(r, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        string exe = Win32RegistryProvider.ExtractExecutablePath(cmd);
                        if (!IsMissingLocalFile(exe)) continue;

                        items.Add(new RegistryIssueItem
                        {
                            CategoryId = cat.Id,
                            CategoryName = cat.Name,
                            ItemType = LeftoverCategory.RegistryValue,
                            RegistryPath = fullPath,
                            ValueName = valueName,
                            TargetDescription = exe,
                            Reason = $"Başlangıç komutu eksik bir dosyaya işaret ediyor: {exe}",
                            Risk = cat.Risk,
                            IsSelected = true
                        });
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }
        }

        return items;
    }

    private static List<RegistryIssueItem> ScanBrokenAppPaths(RegistryCleanCategory cat, CancellationToken ct)
    {
        var items = new List<RegistryIssueItem>();
        (RegistryHive Hive, string SubKey, string Prefix)[] roots =
        [
            (RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\App Paths", "HKCU"),
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths", "HKLM")
        ];

        foreach (var (hive, subKey, prefix) in roots)
        {
            foreach (var view in GetViewsForHive(hive))
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var appPathsRoot = baseKey.OpenSubKey(subKey, writable: false);
                    if (appPathsRoot == null) continue;

                    foreach (string exeSubKey in appPathsRoot.GetSubKeyNames())
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            using var entryKey = appPathsRoot.OpenSubKey(exeSubKey, writable: false);
                            if (entryKey == null) continue;

                            string? defaultVal = entryKey.GetValue(null) as string;
                            if (string.IsNullOrWhiteSpace(defaultVal)) continue;

                            string exe = Win32RegistryProvider.ExtractExecutablePath(defaultVal);
                            if (!IsMissingLocalFile(exe)) continue;

                            items.Add(new RegistryIssueItem
                            {
                                CategoryId = cat.Id,
                                CategoryName = cat.Name,
                                ItemType = LeftoverCategory.RegistryKey,
                                RegistryPath = $"{prefix}\\{subKey}\\{exeSubKey}",
                                ValueName = null,
                                TargetDescription = exe,
                                Reason = $"App Paths kısayolunun hedef dosyası bulunamadı: {exe}",
                                Risk = cat.Risk,
                                IsSelected = true
                            });
                        }
                        catch (OperationCanceledException) { throw; }
                        catch { }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }
        }

        return items;
    }

    private static List<RegistryIssueItem> ScanMissingSharedDlls(RegistryCleanCategory cat, CancellationToken ct)
    {
        var items = new List<RegistryIssueItem>();
        string[] subKeys =
        [
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\SharedDLLs"
        ];

        foreach (string subKey in subKeys)
        {
            ScanValuePathNames(
                RegistryHive.LocalMachine,
                subKey,
                "HKLM",
                rawName => rawName,
                cat,
                items,
                ct,
                reasonPrefix: "SharedDLLs kaydındaki paylaşılan dosya diskte yok");
        }

        return items;
    }

    private static List<RegistryIssueItem> ScanOrphanedFirewallRules(RegistryCleanCategory cat, CancellationToken ct)
    {
        var items = new List<RegistryIssueItem>();
        const string subKey = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules";

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
            using var key = baseKey.OpenSubKey(subKey, writable: false);
            if (key == null) return items;

            string fullPath = $"HKLM\\{subKey}";
            foreach (string valueName in key.GetValueNames())
            {
                ct.ThrowIfCancellationRequested();
                if (key.GetValue(valueName) is not string ruleText || string.IsNullOrWhiteSpace(ruleText))
                    continue;

                string? appPath = ExtractFirewallRuleAppPath(ruleText);
                if (!IsMissingLocalFile(appPath)) continue;

                items.Add(new RegistryIssueItem
                {
                    CategoryId = cat.Id,
                    CategoryName = cat.Name,
                    ItemType = LeftoverCategory.RegistryValue,
                    RegistryPath = fullPath,
                    ValueName = valueName,
                    TargetDescription = appPath!,
                    Reason = $"Güvenlik duvarı kuralının hedef uygulaması silinmiş: {appPath}",
                    Risk = cat.Risk,
                    IsSelected = true
                });
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { }

        return items;
    }

    public static List<RegistryIssueItem> ScanEmptySoftwareKeys(
        RegistryHive hive,
        string softwareRootSubKey,
        RegistryCleanCategory cat,
        CancellationToken ct)
    {
        var items = new List<RegistryIssueItem>();
        string prefix = hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU";

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using var softwareKey = baseKey.OpenSubKey(softwareRootSubKey, writable: false);
            if (softwareKey == null) return items;

            foreach (string vendor in softwareKey.GetSubKeyNames())
            {
                ct.ThrowIfCancellationRequested();
                if (ProtectedVendorSubKeys.Contains(vendor)) continue;

                try
                {
                    using var vendorKey = softwareKey.OpenSubKey(vendor, writable: false);
                    if (vendorKey == null) continue;

                    if (vendorKey.SubKeyCount == 0 && vendorKey.ValueCount == 0)
                    {
                        items.Add(new RegistryIssueItem
                        {
                            CategoryId = cat.Id,
                            CategoryName = cat.Name,
                            ItemType = LeftoverCategory.RegistryKey,
                            RegistryPath = $"{prefix}\\{softwareRootSubKey}\\{vendor}",
                            ValueName = null,
                            TargetDescription = vendor,
                            Reason = "Hiçbir değer veya alt anahtar içermeyen boş yazılım anahtarı",
                            Risk = cat.Risk,
                            IsSelected = false
                        });
                        continue;
                    }

                    foreach (string appName in vendorKey.GetSubKeyNames())
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            using var appKey = vendorKey.OpenSubKey(appName, writable: false);
                            if (appKey != null && appKey.SubKeyCount == 0 && appKey.ValueCount == 0)
                            {
                                items.Add(new RegistryIssueItem
                                {
                                    CategoryId = cat.Id,
                                    CategoryName = cat.Name,
                                    ItemType = LeftoverCategory.RegistryKey,
                                    RegistryPath = $"{prefix}\\{softwareRootSubKey}\\{vendor}\\{appName}",
                                    ValueName = null,
                                    TargetDescription = $"{vendor}\\{appName}",
                                    Reason = "Hiçbir değer veya alt anahtar içermeyen boş alt yazılım anahtarı",
                                    Risk = cat.Risk,
                                    IsSelected = false
                                });
                            }
                        }
                        catch (OperationCanceledException) { throw; }
                        catch { }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { }

        return items;
    }

    public static string? ExtractMuiCachePath(string? valueName)
    {
        if (string.IsNullOrWhiteSpace(valueName)) return null;

        int friendlyIdx = valueName.IndexOf(".FriendlyAppName", StringComparison.OrdinalIgnoreCase);
        if (friendlyIdx > 0) return valueName[..friendlyIdx];

        int companyIdx = valueName.IndexOf(".ApplicationCompany", StringComparison.OrdinalIgnoreCase);
        if (companyIdx > 0) return valueName[..companyIdx];

        return valueName.Contains('\\') ? valueName : null;
    }

    public static string? ExtractFirewallRuleAppPath(string? ruleText)
    {
        if (string.IsNullOrWhiteSpace(ruleText)) return null;

        foreach (string part in ruleText.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith("App=", StringComparison.OrdinalIgnoreCase))
            {
                string val = part[4..].Trim();
                if (val.Equals("System", StringComparison.OrdinalIgnoreCase)) return null;
                return val;
            }
        }
        return null;
    }

    public static bool IsMissingLocalFile(string? rawPath)
    {
        if (!TryNormalizeLocalCandidate(rawPath, out string? fullPath, out string? root))
            return false;

        try
        {
            if (!new DriveInfo(root!).IsReady)
                return false;
            return !File.Exists(fullPath!);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsMissingLocalDirectory(string? rawPath)
    {
        if (!TryNormalizeLocalCandidate(rawPath, out string? fullPath, out string? root))
            return false;

        try
        {
            if (!new DriveInfo(root!).IsReady)
                return false;
            return !Directory.Exists(fullPath!);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryNormalizeLocalCandidate(string? rawPath, out string? normalizedPath, out string? driveRoot)
    {
        normalizedPath = null;
        driveRoot = null;

        if (string.IsNullOrWhiteSpace(rawPath)) return false;

        string cleaned = Environment.ExpandEnvironmentVariables(rawPath.Trim().Trim('"')).Trim();
        if (cleaned.Length < 4) return false;
        if (cleaned.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        if (cleaned.Contains('%') || cleaned.Contains('*') || cleaned.Contains('?')) return false;

        if (!char.IsAsciiLetter(cleaned[0]) || cleaned[1] != ':' || (cleaned[2] != '\\' && cleaned[2] != '/'))
            return false;

        string full;
        try
        {
            full = Path.GetFullPath(cleaned);
        }
        catch
        {
            return false;
        }

        string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";
        if (full.StartsWith(winDir, StringComparison.OrdinalIgnoreCase))
            return false;

        if (full.Contains(@"\Program Files\WindowsApps", StringComparison.OrdinalIgnoreCase))
            return false;

        normalizedPath = full;
        driveRoot = Path.GetPathRoot(full);
        return !string.IsNullOrEmpty(driveRoot);
    }

    private static IEnumerable<RegistryView> GetViewsForHive(RegistryHive hive) =>
        hive == RegistryHive.LocalMachine && Environment.Is64BitOperatingSystem
            ? [RegistryView.Registry64, RegistryView.Registry32]
            : [RegistryView.Default];
}
