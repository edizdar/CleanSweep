using System.Text.RegularExpressions;
using CleanSweep.Core.Models;

namespace CleanSweep.Core.Safety;

public static partial class SafetyGuard
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "app", "apps", "setup", "install", "installer", "uninstall", "uninstaller",
        "update", "updater", "client", "server", "service", "services", "windows",
        "microsoft", "pro", "plus", "free", "lite", "edition", "studio", "tool",
        "tools", "utility", "utilities", "helper", "inc", "llc", "ltd", "corp",
        "corporation", "gmbh", "software", "system", "systems", "common", "files",
        "program", "programs", "x64", "x86", "win32", "win64", "bit", "version",
        "the", "and", "for", "desktop", "launcher", "runtime", "redistributable"
    };

    private static readonly HashSet<string> ProtectedVendorNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "microsoft", "windows", "windowsapps", "packages", "common files",
        "internet explorer", "windows defender", "windows mail", "windows nt",
        "windows media player", "windows powershell", "microsoft.net", "dotnet",
        "google", "adobe", "intel", "amd", "nvidia", "realtek", "oracle", "java"
    };

    private static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "svchost", "csrss", "wininit", "winlogon", "lsass", "services",
        "smss", "system", "idle", "dwm", "conhost", "sihost", "taskhostw",
        "ctfmon", "runtimebroker", "searchhost", "startmenuexperiencehost",
        "shellexperiencehost", "cleansweep", "fontdrvhost", "audiodg", "spoolsv"
    };

    private static readonly string[] ProtectedRegistryKeys =
    [
        @"HKLM",
        @"HKCU",
        @"HKCR",
        @"HKU",
        @"HKLM\SOFTWARE",
        @"HKCU\SOFTWARE",
        @"HKLM\SOFTWARE\MICROSOFT",
        @"HKCU\SOFTWARE\MICROSOFT",
        @"HKLM\SOFTWARE\MICROSOFT\WINDOWS",
        @"HKCU\SOFTWARE\MICROSOFT\WINDOWS",
        @"HKLM\SOFTWARE\MICROSOFT\WINDOWS\CURRENTVERSION",
        @"HKCU\SOFTWARE\MICROSOFT\WINDOWS\CURRENTVERSION",
        @"HKLM\SOFTWARE\MICROSOFT\WINDOWS\CURRENTVERSION\UNINSTALL",
        @"HKCU\SOFTWARE\MICROSOFT\WINDOWS\CURRENTVERSION\UNINSTALL",
        @"HKLM\SOFTWARE\MICROSOFT\WINDOWS\CURRENTVERSION\RUN",
        @"HKCU\SOFTWARE\MICROSOFT\WINDOWS\CURRENTVERSION\RUN",
        @"HKLM\SOFTWARE\MICROSOFT\WINDOWS\CURRENTVERSION\RUNONCE",
        @"HKCU\SOFTWARE\MICROSOFT\WINDOWS\CURRENTVERSION\RUNONCE",
        @"HKLM\SOFTWARE\MICROSOFT\WINDOWS\CURRENTVERSION\APP PATHS",
        @"HKLM\SOFTWARE\WOW6432NODE",
        @"HKLM\SOFTWARE\WOW6432NODE\MICROSOFT",
        @"HKLM\SOFTWARE\WOW6432NODE\MICROSOFT\WINDOWS",
        @"HKLM\SOFTWARE\WOW6432NODE\MICROSOFT\WINDOWS\CURRENTVERSION",
        @"HKLM\SOFTWARE\WOW6432NODE\MICROSOFT\WINDOWS\CURRENTVERSION\UNINSTALL",
        @"HKLM\SOFTWARE\WOW6432NODE\MICROSOFT\WINDOWS\CURRENTVERSION\RUN",
        @"HKLM\SOFTWARE\CLASSES",
        @"HKLM\SOFTWARE\CLASSES\CLSID",
        @"HKLM\SOFTWARE\CLASSES\*\SHELL",
        @"HKLM\SOFTWARE\CLASSES\DIRECTORY\SHELL",
        @"HKLM\SOFTWARE\CLASSES\DIRECTORY\BACKGROUND\SHELL",
        @"HKLM\SOFTWARE\CLASSES\INSTALLER\PRODUCTS",
        @"HKCU\SOFTWARE\CLASSES\INSTALLER\PRODUCTS",
        @"HKCR\CLSID",
        @"HKCR\*\SHELL",
        @"HKLM\SOFTWARE\POLICIES",
        @"HKCU\SOFTWARE\POLICIES",
        @"HKLM\SYSTEM",
        @"HKLM\SYSTEM\CURRENTCONTROLSET",
        @"HKLM\SYSTEM\CURRENTCONTROLSET\SERVICES",
        @"HKLM\SYSTEM\CURRENTCONTROLSET\CONTROL"
    ];

    public static bool IsProtectedProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return true;
        string clean = Path.GetFileNameWithoutExtension(processName.Trim());
        return string.IsNullOrWhiteSpace(clean) || ProtectedProcessNames.Contains(clean);
    }

    public static string NormalizeFilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        string expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        try
        {
            string full = Path.GetFullPath(expanded);
            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return expanded.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    public static string NormalizeRegistryPath(string regPath)
    {
        if (string.IsNullOrWhiteSpace(regPath)) return string.Empty;
        string s = regPath.Trim().Replace('/', '\\').TrimEnd('\\');
        if (s.StartsWith(@"HKEY_LOCAL_MACHINE\", StringComparison.OrdinalIgnoreCase))
            s = @"HKLM\" + s[@"HKEY_LOCAL_MACHINE\".Length..];
        else if (s.Equals("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase))
            s = "HKLM";
        else if (s.StartsWith(@"HKEY_CURRENT_USER\", StringComparison.OrdinalIgnoreCase))
            s = @"HKCU\" + s[@"HKEY_CURRENT_USER\".Length..];
        else if (s.Equals("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase))
            s = "HKCU";
        else if (s.StartsWith(@"HKEY_CLASSES_ROOT\", StringComparison.OrdinalIgnoreCase))
            s = @"HKCR\" + s[@"HKEY_CLASSES_ROOT\".Length..];
        else if (s.Equals("HKEY_CLASSES_ROOT", StringComparison.OrdinalIgnoreCase))
            s = "HKCR";
        else if (s.StartsWith(@"HKEY_USERS\", StringComparison.OrdinalIgnoreCase))
            s = @"HKU\" + s[@"HKEY_USERS\".Length..];
        else if (s.Equals("HKEY_USERS", StringComparison.OrdinalIgnoreCase))
            s = "HKU";
        return s;
    }

    public static bool IsProtectedFilePath(string rawPath)
    {
        string path = NormalizeFilePath(rawPath);
        if (string.IsNullOrWhiteSpace(path)) return true;

        // Drive root e.g. C: or C:\
        if (path.Length <= 3 && path.Contains(':')) return true;

        string? root = Path.GetPathRoot(path)?.TrimEnd('\\');
        if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase)) return true;

        var protectedRoots = GetProtectedFileRoots();
        foreach (string p in protectedRoots)
        {
            if (string.Equals(path, p, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Block anything directly inside Windows or System32 or SysWOW64
        string winDir = NormalizeFilePath(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        if (!string.IsNullOrEmpty(winDir))
        {
            if (string.Equals(path, winDir, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(winDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Block C:\Program Files\WindowsApps (TrustedInstaller ACL protected; MSIX removal uses Remove-AppxPackage)
        string windowsAppsDir = NormalizeFilePath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps"));
        if (!string.IsNullOrEmpty(windowsAppsDir) &&
            (string.Equals(path, windowsAppsDir, StringComparison.OrdinalIgnoreCase) ||
             path.StartsWith(windowsAppsDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Protect immediate generic vendor roots inside Program Files / ProgramData / AppData
        string? parent = Path.GetDirectoryName(path);
        string leaf = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(leaf))
        {
            string normParent = NormalizeFilePath(parent);
            if (protectedRoots.Contains(normParent, StringComparer.OrdinalIgnoreCase) &&
                ProtectedVendorNames.Contains(leaf))
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> GetProtectedFileRoots()
    {
        var roots = new List<string>();
        void Add(string? p)
        {
            string n = NormalizeFilePath(p ?? string.Empty);
            if (!string.IsNullOrEmpty(n)) roots.Add(n);
        }

        Add(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.System));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.Programs));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu));
        Add(@"C:\Users");
        Add(@"C:\ProgramData");
        Add(@"C:\Program Files");
        Add(@"C:\Program Files (x86)");
        Add(@"C:\Program Files\Common Files");
        Add(@"C:\Program Files (x86)\Common Files");
        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages"));
        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"));
        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft"));
        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft"));

        string localLow = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow");
        Add(localLow);
        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData"));

        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool IsProtectedRegistryPath(string rawRegPath, bool isValueTarget = false)
    {
        string path = NormalizeRegistryPath(rawRegPath);
        if (string.IsNullOrWhiteSpace(path)) return true;

        if (!isValueTarget)
        {
            foreach (string p in ProtectedRegistryKeys)
            {
                if (string.Equals(path, p, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        else
        {
            // Even for values, never allow hive roots or empty subkey
            if (!path.Contains('\\')) return true;
        }

        // Prevent deleting top-level vendor keys like HKLM\SOFTWARE\Microsoft\*?
        // Allow specific app keys under Microsoft only if depth >= 4 (e.g. HKCU\Software\Microsoft\VSCode)
        string[] segments = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length <= 2) return true; // e.g. HKLM\SOFTWARE or HKCU\Software

        // Block HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\<anything directly critical>
        if (!isValueTarget &&
            path.StartsWith(@"HKLM\SOFTWARE\MICROSOFT\WINDOWS NT\CURRENTVERSION", StringComparison.OrdinalIgnoreCase) &&
            segments.Length <= 6)
        {
            return true;
        }

        return false;
    }

    public static bool IsItemAllowed(LeftoverItem item)
    {
        return item.Category switch
        {
            LeftoverCategory.File or LeftoverCategory.Directory => !IsProtectedFilePath(item.Path),
            LeftoverCategory.RegistryKey => !IsProtectedRegistryPath(item.Path, isValueTarget: false),
            LeftoverCategory.RegistryValue => !string.IsNullOrWhiteSpace(item.ValueName) &&
                                              !IsProtectedRegistryPath(item.Path, isValueTarget: true),
            LeftoverCategory.Service => !string.IsNullOrWhiteSpace(item.Path) &&
                                        !item.Path.Equals("svchost", StringComparison.OrdinalIgnoreCase),
            LeftoverCategory.ScheduledTask => !string.IsNullOrWhiteSpace(item.Path) &&
                                              !item.Path.StartsWith(@"\Microsoft\Windows\", StringComparison.OrdinalIgnoreCase),
            LeftoverCategory.EnvironmentPath => !IsProtectedFilePath(item.Path),
            LeftoverCategory.FirewallRule => !string.IsNullOrWhiteSpace(item.ValueName),
            _ => false
        };
    }

    public static List<InstalledApp> GetOtherAppsFromSamePublisher(InstalledApp target, IEnumerable<InstalledApp> allApps)
    {
        string pub = NormalizePublisher(target.Publisher);
        if (string.IsNullOrWhiteSpace(pub)) return [];

        return allApps
            .Where(a => !string.Equals(a.Id, target.Id, StringComparison.OrdinalIgnoreCase))
            .Where(a => string.Equals(NormalizePublisher(a.Publisher), pub, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static string NormalizePublisher(string? publisher)
    {
        if (string.IsNullOrWhiteSpace(publisher)) return string.Empty;
        string cleaned = publisher.Trim();
        cleaned = PublisherSuffixRegex().Replace(cleaned, string.Empty);
        return cleaned.Trim(' ', ',', '.', '-').ToLowerInvariant();
    }

    public static List<string> ExtractSearchTokens(InstalledApp app)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddNameParts(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            string cleaned = VersionParenRegex().Replace(raw, " ").Trim();
            if (cleaned.Length >= 3 && !StopWords.Contains(cleaned))
                tokens.Add(cleaned);

            foreach (string part in TokenSplitRegex().Split(cleaned))
            {
                if (part.Length >= 4 && !StopWords.Contains(part) && !int.TryParse(part, out _))
                    tokens.Add(part);
            }
        }

        AddNameParts(app.Name);

        if (!string.IsNullOrWhiteSpace(app.InstallLocation))
        {
            string dirName = Path.GetFileName(NormalizeFilePath(app.InstallLocation));
            if (dirName.Length >= 3 && !StopWords.Contains(dirName))
                tokens.Add(dirName);
        }

        return tokens.OrderByDescending(t => t.Length).ToList();
    }

    [GeneratedRegex(@"\b(Inc\.?|LLC|Ltd\.?|Corp\.?|Corporation|GmbH|Co\.?|S\.A\.?|AB|Oy|B\.V\.?|Pte)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PublisherSuffixRegex();

    [GeneratedRegex(@"\s*\([^)]*\)|\s+\d+(\.\d+)+.*$")]
    private static partial Regex VersionParenRegex();

    [GeneratedRegex(@"[\s\-_.:/\\]+")]
    private static partial Regex TokenSplitRegex();
}
