using CleanSweep.Core.Safety;
using Microsoft.Win32;

namespace CleanSweep.Core.Tracing;

public sealed class SystemSnapshot
{
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
    public HashSet<string> Directories { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Files { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> RegistryKeys { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, HashSet<string>> RegistryValues { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public static SystemSnapshot Capture()
    {
        var snap = new SystemSnapshot();

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] watchedDirs =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Path.Combine(userProfile, "AppData", "LocalLow"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
        ];

        foreach (string root in watchedDirs.Where(d => !string.IsNullOrWhiteSpace(d) && Directory.Exists(d)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            CaptureDirectoryShallow(root, depth: 0, maxDepth: 2, snap);
        }

        CaptureRegistryKeys(RegistryHive.CurrentUser, RegistryView.Registry64, "HKCU", @"Software", snap, maxDepth: 2);
        CaptureRegistryKeys(RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM", @"SOFTWARE", snap, maxDepth: 2);
        CaptureRegistryKeys(RegistryHive.LocalMachine, RegistryView.Registry32, @"HKLM\SOFTWARE\WOW6432Node", @"SOFTWARE", snap, maxDepth: 2);
        CaptureRegistryKeys(RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM", @"SYSTEM\CurrentControlSet\Services", snap, maxDepth: 1);

        CaptureRegistryValueNames(RegistryHive.CurrentUser, RegistryView.Registry64, "HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", snap);
        CaptureRegistryValueNames(RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", snap);

        return snap;
    }

    private static void CaptureDirectoryShallow(string dir, int depth, int maxDepth, SystemSnapshot snap)
    {
        if (depth >= maxDepth) return;
        try
        {
            foreach (string sub in Directory.EnumerateDirectories(dir))
            {
                string norm = SafetyGuard.NormalizeFilePath(sub);
                snap.Directories.Add(norm);
                if (depth + 1 < maxDepth)
                    CaptureDirectoryShallow(norm, depth + 1, maxDepth, snap);
            }

            foreach (string file in Directory.EnumerateFiles(dir))
            {
                snap.Files.Add(SafetyGuard.NormalizeFilePath(file));
            }
        }
        catch { }
    }

    private static void CaptureRegistryKeys(
        RegistryHive hive,
        RegistryView view,
        string displayPrefix,
        string subPath,
        SystemSnapshot snap,
        int maxDepth)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(subPath, writable: false);
            if (key == null) return;

            string rootDisplay = displayPrefix.Contains('\\') ? displayPrefix : $@"{displayPrefix}\{subPath}";
            CaptureRegRecursive(key, rootDisplay, depth: 0, maxDepth, snap);
        }
        catch { }
    }

    private static void CaptureRegRecursive(RegistryKey key, string displayPath, int depth, int maxDepth, SystemSnapshot snap)
    {
        if (depth >= maxDepth) return;
        try
        {
            foreach (string sub in key.GetSubKeyNames())
            {
                string childPath = $@"{displayPath}\{sub}";
                snap.RegistryKeys.Add(childPath);
                if (depth + 1 < maxDepth)
                {
                    try
                    {
                        using var subKey = key.OpenSubKey(sub, writable: false);
                        if (subKey != null)
                            CaptureRegRecursive(subKey, childPath, depth + 1, maxDepth, snap);
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    private static void CaptureRegistryValueNames(
        RegistryHive hive,
        RegistryView view,
        string hivePrefix,
        string subPath,
        SystemSnapshot snap)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(subPath, writable: false);
            if (key == null) return;

            string fullKey = $@"{hivePrefix}\{subPath}";
            var set = new HashSet<string>(key.GetValueNames().Where(n => !string.IsNullOrEmpty(n)), StringComparer.OrdinalIgnoreCase);
            snap.RegistryValues[fullKey] = set;
        }
        catch { }
    }
}
