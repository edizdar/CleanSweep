using CleanSweep.Core.Models;
using CleanSweep.Core.Safety;
using Microsoft.Win32;

namespace CleanSweep.Core.Scanning;

public static class RegistryScanner
{
    public static List<LeftoverItem> Scan(
        InstalledApp target,
        IReadOnlyList<string> searchTokens,
        bool hasOtherSamePublisherApps,
        CancellationToken ct,
        ScanOptions? options = null)
    {
        options ??= new ScanOptions();
        var items = new List<LeftoverItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string normAppName = target.Name.Trim();
        string normPub = SafetyGuard.NormalizePublisher(target.Publisher);
        string installLoc = SafetyGuard.NormalizeFilePath(target.InstallLocation);

        // 1. Direct or orphaned Uninstall entry
        if (!string.IsNullOrWhiteSpace(target.RegistryKeyPath))
        {
            string normReg = SafetyGuard.NormalizeRegistryPath(target.RegistryKeyPath);
            if (!SafetyGuard.IsProtectedRegistryPath(normReg) && seen.Add($"KEY|{normReg}"))
            {
                var item = new LeftoverItem
                {
                    Category = LeftoverCategory.RegistryKey,
                    Path = normReg
                };
                if (options.IsForcedUninstall)
                {
                    RiskScorer.ApplyScore(item, MatchStrength.InstallLocationExact, hasOtherSamePublisherApps,
                        "Zorla Kaldırma: Program Ekle/Kaldır listesindeki bozuk veya kaldırılamayan kayıt.");
                    item.Confidence = 95;
                }
                else
                {
                    RiskScorer.ApplyScore(item, MatchStrength.ExactAppNameMatch, hasOtherSamePublisherApps,
                        "Uygulamanın kayıtlı Uninstall (Kaldırma) kayıt defteri anahtarı.");
                    item.Confidence = 75;
                    item.Risk = RiskLevel.Moderate;
                    item.IsSelected = false;
                }
                items.Add(item);
            }
        }
        else if (options.IsForcedUninstall)
        {
            ScanOrphanedUninstallKeys(target, installLoc, searchTokens, options, seen, items);
        }

        // 2. Scan HKCU\Software, HKLM\SOFTWARE, HKLM\SOFTWARE\WOW6432Node
        ScanSoftwareRoot(RegistryHive.CurrentUser, RegistryView.Registry64, "HKCU", "Software", normAppName, normPub, searchTokens, hasOtherSamePublisherApps, seen, items, ct);
        ScanSoftwareRoot(RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM", "SOFTWARE", normAppName, normPub, searchTokens, hasOtherSamePublisherApps, seen, items, ct);
        ScanSoftwareRoot(RegistryHive.LocalMachine, RegistryView.Registry32, @"HKLM\SOFTWARE\WOW6432Node", "SOFTWARE", normAppName, normPub, searchTokens, hasOtherSamePublisherApps, seen, items, ct);

        // 3. Run / RunOnce values (value-level only!)
        ScanRunKey(RegistryHive.CurrentUser, RegistryView.Registry64, "HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", target, installLoc, searchTokens, seen, items);
        ScanRunKey(RegistryHive.CurrentUser, RegistryView.Registry64, "HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", target, installLoc, searchTokens, seen, items);
        ScanRunKey(RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", target, installLoc, searchTokens, seen, items);
        ScanRunKey(RegistryHive.LocalMachine, RegistryView.Registry32, @"HKLM", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", target, installLoc, searchTokens, seen, items);

        // 4. App Paths
        ScanAppPaths(RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM", installLoc, seen, items);
        ScanAppPaths(RegistryHive.CurrentUser, RegistryView.Registry64, "HKCU", installLoc, seen, items);

        // 5. Moderate & Advanced: Broken MSI Installer records, MuiCache, AppCompatFlags, SharedDLLs
        if (options.Depth is ScanDepth.Moderate or ScanDepth.Advanced)
        {
            ScanInstallerProducts(RegistryHive.LocalMachine, "HKLM", @"SOFTWARE\Classes\Installer\Products", normAppName, searchTokens, options, seen, items, ct);
            ScanInstallerProducts(RegistryHive.CurrentUser, "HKCU", @"Software\Classes\Installer\Products", normAppName, searchTokens, options, seen, items, ct);
            ScanUserDataMsiProducts(normAppName, installLoc, options, seen, items, ct);
            ScanPathReferencingValues(RegistryHive.CurrentUser, "HKCU", @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache", installLoc, "MuiCache uygulama önbellek kaydı.", seen, items);
            ScanPathReferencingValues(RegistryHive.CurrentUser, "HKCU", @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers", installLoc, "Uyumluluk (AppCompat) kaydı.", seen, items);
            ScanPathReferencingValues(RegistryHive.CurrentUser, "HKCU", @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Compatibility Assistant\Store", installLoc, "Program Uyumluluk Yardımcısı önbellek kaydı.", seen, items);
            ScanPathReferencingValues(RegistryHive.LocalMachine, "HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs", installLoc, "SharedDLLs paylaşım referans kaydı.", seen, items);
        }

        // 6. Advanced: Deep HKCR / CLSID COM & Shell Context Menu leftovers
        if (options.Depth == ScanDepth.Advanced)
        {
            ScanDeepComAndShell(installLoc, normAppName, searchTokens, seen, items, ct);
        }

        return items;
    }

    private static void ScanOrphanedUninstallKeys(
        InstalledApp target,
        string installLoc,
        IReadOnlyList<string> tokens,
        ScanOptions options,
        HashSet<string> seen,
        List<LeftoverItem> items)
    {
        const string sub = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        var scopes = new (RegistryHive Hive, RegistryView View, string Prefix)[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64, $@"HKLM\{sub}"),
            (RegistryHive.LocalMachine, RegistryView.Registry32, $@"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (RegistryHive.CurrentUser, RegistryView.Registry64, $@"HKCU\{sub}")
        };

        foreach (var (hive, view, prefix) in scopes)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstallKey = baseKey.OpenSubKey(sub, writable: false);
                if (uninstallKey == null) continue;

                foreach (string child in uninstallKey.GetSubKeyNames())
                {
                    using var appKey = uninstallKey.OpenSubKey(child, writable: false);
                    if (appKey == null) continue;

                    string displayName = appKey.GetValue("DisplayName")?.ToString() ?? string.Empty;
                    string loc = appKey.GetValue("InstallLocation")?.ToString() ?? string.Empty;
                    string uninst = appKey.GetValue("UninstallString")?.ToString() ?? string.Empty;

                    bool locMatch = !string.IsNullOrEmpty(installLoc) &&
                                    (loc.Contains(installLoc, StringComparison.OrdinalIgnoreCase) ||
                                     uninst.Contains(installLoc, StringComparison.OrdinalIgnoreCase));
                    bool nameMatch = !string.IsNullOrWhiteSpace(displayName) &&
                                     MatchesAppOrToken(displayName, target.Name, tokens, out bool exact) && exact;

                    if (locMatch || nameMatch)
                    {
                        string fullPath = $@"{prefix}\{child}";
                        if (!SafetyGuard.IsProtectedRegistryPath(fullPath) && seen.Add($"KEY|{fullPath}"))
                        {
                            var item = new LeftoverItem
                            {
                                Category = LeftoverCategory.RegistryKey,
                                Path = fullPath
                            };
                            RiskScorer.ApplyScore(item, MatchStrength.InstallLocationExact,
                                customReason: "Zorla Kaldırma: Program Ekle/Kaldır listesindeki bozuk/kaldırılamayan kayıt.");
                            item.Confidence = 95;
                            items.Add(item);
                        }
                    }
                }
            }
            catch { }
        }
    }

    private static void ScanInstallerProducts(
        RegistryHive hive,
        string hiveLabel,
        string subPath,
        string normAppName,
        IReadOnlyList<string> tokens,
        ScanOptions options,
        HashSet<string> seen,
        List<LeftoverItem> items,
        CancellationToken ct)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var productsKey = baseKey.OpenSubKey(subPath, writable: false);
            if (productsKey == null) return;

            foreach (string prodId in productsKey.GetSubKeyNames())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var prod = productsKey.OpenSubKey(prodId, writable: false);
                    string prodName = prod?.GetValue("ProductName")?.ToString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(prodName) &&
                        MatchesAppOrToken(prodName, normAppName, tokens, out bool exact) && exact)
                    {
                        string fullPath = $@"{hiveLabel}\{subPath}\{prodId}";
                        if (!SafetyGuard.IsProtectedRegistryPath(fullPath) && seen.Add($"KEY|{fullPath}"))
                        {
                            var item = new LeftoverItem
                            {
                                Category = LeftoverCategory.RegistryKey,
                                Path = fullPath
                            };
                            RiskScorer.ApplyScore(item, options.IsForcedUninstall ? MatchStrength.ExactAppNameMatch : MatchStrength.PartialTokenMatch,
                                customReason: $"Windows Installer (MSI) ürün kaydı ({prodName}).");
                            if (options.IsForcedUninstall)
                            {
                                item.Confidence = 90;
                                item.Risk = RiskLevel.Safe;
                                item.IsSelected = true;
                            }
                            items.Add(item);
                        }
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    private static void ScanUserDataMsiProducts(
        string normAppName,
        string installLoc,
        ScanOptions options,
        HashSet<string> seen,
        List<LeftoverItem> items,
        CancellationToken ct)
    {
        const string subPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UserData\S-1-5-18\Products";
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var prods = baseKey.OpenSubKey(subPath, writable: false);
            if (prods == null) return;

            foreach (string prodId in prods.GetSubKeyNames())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var props = prods.OpenSubKey($@"{prodId}\InstallProperties", writable: false);
                    if (props == null) continue;
                    string disp = props.GetValue("DisplayName")?.ToString() ?? string.Empty;
                    string loc = props.GetValue("InstallLocation")?.ToString() ?? string.Empty;

                    bool matchLoc = !string.IsNullOrEmpty(installLoc) && loc.Contains(installLoc, StringComparison.OrdinalIgnoreCase);
                    bool matchName = !string.IsNullOrEmpty(disp) && MatchesAppOrToken(disp, normAppName, [], out bool ex) && ex;

                    if (matchLoc || matchName)
                    {
                        string fullPath = $@"HKLM\{subPath}\{prodId}";
                        if (!SafetyGuard.IsProtectedRegistryPath(fullPath) && seen.Add($"KEY|{fullPath}"))
                        {
                            var item = new LeftoverItem
                            {
                                Category = LeftoverCategory.RegistryKey,
                                Path = fullPath
                            };
                            RiskScorer.ApplyScore(item, options.IsForcedUninstall ? MatchStrength.InstallLocationExact : MatchStrength.PartialTokenMatch,
                                customReason: $"Windows Installer UserData MSI paketi ({disp}).");
                            items.Add(item);
                        }
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    private static void ScanPathReferencingValues(
        RegistryHive hive,
        string hiveLabel,
        string subPath,
        string installLoc,
        string reason,
        HashSet<string> seen,
        List<LeftoverItem> items)
    {
        if (string.IsNullOrWhiteSpace(installLoc)) return;
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(subPath, writable: false);
            if (key == null) return;

            string fullKey = $@"{hiveLabel}\{subPath}";
            foreach (string valueName in key.GetValueNames())
            {
                if (string.IsNullOrWhiteSpace(valueName)) continue;
                string valData = key.GetValue(valueName)?.ToString() ?? string.Empty;
                if (valueName.Contains(installLoc, StringComparison.OrdinalIgnoreCase) ||
                    valData.Contains(installLoc, StringComparison.OrdinalIgnoreCase))
                {
                    if (seen.Add($"VAL|{fullKey}|{valueName}"))
                    {
                        var item = new LeftoverItem
                        {
                            Category = LeftoverCategory.RegistryValue,
                            Path = fullKey,
                            ValueName = valueName
                        };
                        RiskScorer.ApplyScore(item, MatchStrength.PathReferenceExact, customReason: reason);
                        items.Add(item);
                    }
                }
            }
        }
        catch { }
    }

    private static void ScanDeepComAndShell(
        string installLoc,
        string normAppName,
        IReadOnlyList<string> tokens,
        HashSet<string> seen,
        List<LeftoverItem> items,
        CancellationToken ct)
    {
        // Shell context menu integrations (*\shell, Directory\shell, Directory\Background\shell)
        string[] shellRoots =
        [
            @"SOFTWARE\Classes\*\shell",
            @"SOFTWARE\Classes\Directory\shell",
            @"SOFTWARE\Classes\Directory\Background\shell"
        ];

        foreach (string sRoot in shellRoots)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var shellKey = baseKey.OpenSubKey(sRoot, writable: false);
                if (shellKey == null) continue;

                foreach (string verb in shellKey.GetSubKeyNames())
                {
                    ct.ThrowIfCancellationRequested();
                    using var verbKey = shellKey.OpenSubKey(verb, writable: false);
                    using var cmdKey = verbKey?.OpenSubKey("command", writable: false);
                    string cmd = cmdKey?.GetValue(string.Empty)?.ToString() ?? string.Empty;
                    string icon = verbKey?.GetValue("Icon")?.ToString() ?? string.Empty;

                    bool pathHit = !string.IsNullOrEmpty(installLoc) &&
                                   (cmd.Contains(installLoc, StringComparison.OrdinalIgnoreCase) ||
                                    icon.Contains(installLoc, StringComparison.OrdinalIgnoreCase));
                    bool nameHit = MatchesAppOrToken(verb, normAppName, tokens, out bool exact) && exact;

                    if (pathHit || nameHit)
                    {
                        string fullPath = $@"HKLM\{sRoot}\{verb}";
                        if (!SafetyGuard.IsProtectedRegistryPath(fullPath) && seen.Add($"KEY|{fullPath}"))
                        {
                            var item = new LeftoverItem
                            {
                                Category = LeftoverCategory.RegistryKey,
                                Path = fullPath
                            };
                            RiskScorer.ApplyScore(item, pathHit ? MatchStrength.PathReferenceExact : MatchStrength.PartialTokenMatch,
                                customReason: "Sağ tık (Shell Context Menu) entegrasyonu kalıntısı.");
                            items.Add(item);
                        }
                    }
                }
            }
            catch { }
        }

        // COM CLSID scan referencing exact InstallLocation
        if (string.IsNullOrEmpty(installLoc)) return;
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            string clsidSub = view == RegistryView.Registry32
                ? @"SOFTWARE\WOW6432Node\Classes\CLSID"
                : @"SOFTWARE\Classes\CLSID";
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var clsidRoot = baseKey.OpenSubKey(clsidSub, writable: false);
                if (clsidRoot == null) continue;

                foreach (string clsid in clsidRoot.GetSubKeyNames())
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        using var cKey = clsidRoot.OpenSubKey(clsid, writable: false);
                        if (cKey == null) continue;
                        string inproc = cKey.OpenSubKey("InprocServer32")?.GetValue(string.Empty)?.ToString() ?? string.Empty;
                        string local = cKey.OpenSubKey("LocalServer32")?.GetValue(string.Empty)?.ToString() ?? string.Empty;
                        if (inproc.Contains(installLoc, StringComparison.OrdinalIgnoreCase) ||
                            local.Contains(installLoc, StringComparison.OrdinalIgnoreCase))
                        {
                            string fullPath = $@"HKLM\{clsidSub}\{clsid}";
                            if (!SafetyGuard.IsProtectedRegistryPath(fullPath) && seen.Add($"KEY|{fullPath}"))
                            {
                                var item = new LeftoverItem
                                {
                                    Category = LeftoverCategory.RegistryKey,
                                    Path = fullPath
                                };
                                RiskScorer.ApplyScore(item, MatchStrength.PathReferenceExact,
                                    customReason: "COM / CLSID bileşen kaydı uygulamanın kurulum klasörüne işaret ediyor.");
                                items.Add(item);
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    private static void ScanSoftwareRoot(
        RegistryHive hive,
        RegistryView view,
        string prefixLabel,
        string subKey,
        string normAppName,
        string normPub,
        IReadOnlyList<string> searchTokens,
        bool hasOtherSamePublisherApps,
        HashSet<string> seen,
        List<LeftoverItem> items,
        CancellationToken ct)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var softwareKey = baseKey.OpenSubKey(subKey, writable: false);
            if (softwareKey == null) return;

            string rootDisplay = prefixLabel == "HKCU" ? @"HKCU\Software" :
                                 prefixLabel == "HKLM" ? @"HKLM\SOFTWARE" : prefixLabel;

            foreach (string child in softwareKey.GetSubKeyNames())
            {
                ct.ThrowIfCancellationRequested();
                string fullPath = $"{rootDisplay}\\{child}";
                if (SafetyGuard.IsProtectedRegistryPath(fullPath)) continue;

                if (MatchesAppOrToken(child, normAppName, searchTokens, out bool isExact))
                {
                    if (seen.Add($"KEY|{fullPath}"))
                    {
                        var item = new LeftoverItem { Category = LeftoverCategory.RegistryKey, Path = fullPath };
                        RiskScorer.ApplyScore(item, isExact ? MatchStrength.ExactAppNameMatch : MatchStrength.PartialTokenMatch, hasOtherSamePublisherApps);
                        items.Add(item);
                    }
                    continue;
                }

                if (!string.IsNullOrEmpty(normPub) &&
                    SafetyGuard.NormalizePublisher(child).Equals(normPub, StringComparison.OrdinalIgnoreCase))
                {
                    if (!hasOtherSamePublisherApps)
                    {
                        if (seen.Add($"KEY|{fullPath}"))
                        {
                            var item = new LeftoverItem { Category = LeftoverCategory.RegistryKey, Path = fullPath };
                            RiskScorer.ApplyScore(item, MatchStrength.SharedPublisherFolder, hasOtherSamePublisherApps: false);
                            items.Add(item);
                        }
                    }
                    else
                    {
                        try
                        {
                            using var pubKey = softwareKey.OpenSubKey(child, writable: false);
                            if (pubKey == null) continue;
                            foreach (string grandChild in pubKey.GetSubKeyNames())
                            {
                                string subFullPath = $"{fullPath}\\{grandChild}";
                                if (!SafetyGuard.IsProtectedRegistryPath(subFullPath) &&
                                    MatchesAppOrToken(grandChild, normAppName, searchTokens, out bool subExact) &&
                                    seen.Add($"KEY|{subFullPath}"))
                                {
                                    var item = new LeftoverItem { Category = LeftoverCategory.RegistryKey, Path = subFullPath };
                                    RiskScorer.ApplyScore(item, subExact ? MatchStrength.ExactAppNameMatch : MatchStrength.PartialTokenMatch, hasOtherSamePublisherApps);
                                    items.Add(item);
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }
    }

    private static void ScanRunKey(
        RegistryHive hive,
        RegistryView view,
        string hiveLabel,
        string subKeyPath,
        InstalledApp target,
        string installLoc,
        IReadOnlyList<string> searchTokens,
        HashSet<string> seen,
        List<LeftoverItem> items)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var runKey = baseKey.OpenSubKey(subKeyPath, writable: false);
            if (runKey == null) return;

            string fullKey = $@"{hiveLabel}\{subKeyPath}";
            foreach (string valueName in runKey.GetValueNames())
            {
                if (string.IsNullOrWhiteSpace(valueName)) continue;
                string valueData = runKey.GetValue(valueName)?.ToString() ?? string.Empty;

                bool pathRef = !string.IsNullOrWhiteSpace(installLoc) &&
                               valueData.Contains(installLoc, StringComparison.OrdinalIgnoreCase);

                bool nameMatch = MatchesAppOrToken(valueName, target.Name, searchTokens, out _);

                if ((pathRef || nameMatch) && seen.Add($"VAL|{fullKey}|{valueName}"))
                {
                    var item = new LeftoverItem
                    {
                        Category = LeftoverCategory.RegistryValue,
                        Path = fullKey,
                        ValueName = valueName
                    };
                    RiskScorer.ApplyScore(
                        item,
                        pathRef ? MatchStrength.PathReferenceExact : MatchStrength.PartialTokenMatch,
                        customReason: pathRef
                            ? "Başlangıç (Run) kaydı doğrudan uygulamanın kurulum yolunu çalıştırıyor."
                            : "Başlangıç (Run) kaydı uygulama adıyla eşleşiyor.");
                    items.Add(item);
                }
            }
        }
        catch { }
    }

    private static void ScanAppPaths(
        RegistryHive hive,
        RegistryView view,
        string hiveLabel,
        string installLoc,
        HashSet<string> seen,
        List<LeftoverItem> items)
    {
        if (string.IsNullOrWhiteSpace(installLoc)) return;
        const string sub = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var appPaths = baseKey.OpenSubKey(sub, writable: false);
            if (appPaths == null) return;

            foreach (string exeKeyName in appPaths.GetSubKeyNames())
            {
                using var exeKey = appPaths.OpenSubKey(exeKeyName, writable: false);
                if (exeKey == null) continue;
                string defaultVal = exeKey.GetValue(string.Empty)?.ToString() ?? string.Empty;
                string pathVal = exeKey.GetValue("Path")?.ToString() ?? string.Empty;

                if (defaultVal.Contains(installLoc, StringComparison.OrdinalIgnoreCase) ||
                    pathVal.Contains(installLoc, StringComparison.OrdinalIgnoreCase))
                {
                    string fullKey = $@"{hiveLabel}\{sub}\{exeKeyName}";
                    if (!SafetyGuard.IsProtectedRegistryPath(fullKey) && seen.Add($"KEY|{fullKey}"))
                    {
                        var item = new LeftoverItem { Category = LeftoverCategory.RegistryKey, Path = fullKey };
                        RiskScorer.ApplyScore(item, MatchStrength.PathReferenceExact,
                            customReason: "App Paths kaydı doğrudan uygulamanın kurulum klasörüne işaret ediyor.");
                        items.Add(item);
                    }
                }
            }
        }
        catch { }
    }

    private static bool MatchesAppOrToken(string candidate, string appName, IReadOnlyList<string> tokens, out bool isExact)
    {
        isExact = false;
        if (candidate.Equals(appName, StringComparison.OrdinalIgnoreCase))
        {
            isExact = true;
            return true;
        }

        foreach (string token in tokens)
        {
            if (token.Length < 4) continue;
            if (candidate.Equals(token, StringComparison.OrdinalIgnoreCase))
            {
                isExact = true;
                return true;
            }
            if (token.Length >= 6 && candidate.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                isExact = false;
                return true;
            }
        }

        return false;
    }
}
