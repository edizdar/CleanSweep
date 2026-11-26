using CleanSweep.Core.Discovery;
using CleanSweep.Core.Models;
using Microsoft.Win32;

namespace CleanSweep.Core.Startup;

public sealed class AutorunService
{
    private const string RunSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRunSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ApprovedRun32SubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";
    private const string ApprovedStartupFolderSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    public IReadOnlyList<AutorunEntry> GetEntries()
    {
        var entries = new List<AutorunEntry>();

        ReadRunKey(RegistryHive.CurrentUser, RegistryView.Registry64, @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            "Kullanıcı Kayıt Defteri (HKCU\\Run)", AutorunScope.HkcuRun, ApprovedRunSubKey, entries);

        ReadRunKey(RegistryHive.LocalMachine, RegistryView.Registry64, @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            "Sistem Kayıt Defteri (HKLM\\Run)", AutorunScope.HklmRun, ApprovedRunSubKey, entries);

        ReadRunKey(RegistryHive.LocalMachine, RegistryView.Registry32, @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run",
            "Sistem 32-bit (WOW6432Node\\Run)", AutorunScope.HklmWow64Run, ApprovedRun32SubKey, entries);

        ReadStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            RegistryHive.CurrentUser, "Kullanıcı Başlangıç Klasörü", AutorunScope.UserStartupFolder, entries);

        ReadStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
            RegistryHive.LocalMachine, "Ortak Başlangıç Klasörü", AutorunScope.CommonStartupFolder, entries);

        return entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool IsApprovedEnabled(byte[]? rawData)
    {
        if (rawData == null || rawData.Length == 0) return true;
        // Windows StartupApproved: 0x02 or 0x06 = Enabled; 0x03 or 0x07 = Disabled
        return rawData[0] is 0x02 or 0x06;
    }

    public bool SetEnabled(AutorunEntry entry, bool enabled)
    {
        try
        {
            var (hive, approvedSubKey, valueName) = ResolveApprovalTarget(entry);
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.CreateSubKey(approvedSubKey, writable: true);
            if (key == null) return false;

            byte[] payload = new byte[12];
            payload[0] = enabled ? (byte)0x02 : (byte)0x03;
            if (!enabled)
            {
                long fileTime = DateTime.UtcNow.ToFileTimeUtc();
                BitConverter.GetBytes(fileTime).CopyTo(payload, 4);
            }

            key.SetValue(valueName, payload, RegistryValueKind.Binary);
            entry.IsEnabled = enabled;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool DeleteEntry(AutorunEntry entry)
    {
        try
        {
            if (entry.Scope is AutorunScope.UserStartupFolder or AutorunScope.CommonStartupFolder)
            {
                if (File.Exists(entry.FilePath))
                {
                    File.Delete(entry.FilePath);
                    return true;
                }
                return false;
            }

            var (hive, view) = entry.Scope switch
            {
                AutorunScope.HkcuRun => (RegistryHive.CurrentUser, RegistryView.Registry64),
                AutorunScope.HklmWow64Run => (RegistryHive.LocalMachine, RegistryView.Registry32),
                _ => (RegistryHive.LocalMachine, RegistryView.Registry64)
            };

            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var runKey = baseKey.OpenSubKey(RunSubKey, writable: true);
            runKey?.DeleteValue(entry.Name, throwOnMissingValue: false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ReadRunKey(
        RegistryHive hive,
        RegistryView view,
        string fullRegPath,
        string locationLabel,
        AutorunScope scope,
        string approvedSubKey,
        List<AutorunEntry> entries)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var runKey = baseKey.OpenSubKey(RunSubKey, writable: false);
            if (runKey == null) return;

            using var base64 = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var approvedKey = base64.OpenSubKey(approvedSubKey, writable: false);

            foreach (string name in runKey.GetValueNames())
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                string command = runKey.GetValue(name)?.ToString() ?? string.Empty;
                byte[]? approvedBytes = approvedKey?.GetValue(name) as byte[];

                entries.Add(new AutorunEntry
                {
                    Id = $"{scope}:{name}",
                    Name = name,
                    Command = command,
                    ExecutablePath = Win32RegistryProvider.ExtractExecutablePath(command),
                    Location = locationLabel,
                    Scope = scope,
                    IsEnabled = IsApprovedEnabled(approvedBytes),
                    RegistryPath = fullRegPath
                });
            }
        }
        catch { }
    }

    private static void ReadStartupFolder(
        string folder,
        RegistryHive hive,
        string locationLabel,
        AutorunScope scope,
        List<AutorunEntry> entries)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;

        try
        {
            using var base64 = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var approvedKey = base64.OpenSubKey(ApprovedStartupFolderSubKey, writable: false);

            foreach (string file in Directory.EnumerateFiles(folder))
            {
                string fileName = Path.GetFileName(file);
                if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                byte[]? approvedBytes = approvedKey?.GetValue(fileName) as byte[];

                entries.Add(new AutorunEntry
                {
                    Id = $"{scope}:{fileName}",
                    Name = Path.GetFileNameWithoutExtension(file),
                    Command = file,
                    ExecutablePath = file,
                    Location = locationLabel,
                    Scope = scope,
                    IsEnabled = IsApprovedEnabled(approvedBytes),
                    FilePath = file
                });
            }
        }
        catch { }
    }

    private static (RegistryHive Hive, string ApprovedSubKey, string ValueName) ResolveApprovalTarget(AutorunEntry entry)
    {
        return entry.Scope switch
        {
            AutorunScope.HkcuRun => (RegistryHive.CurrentUser, ApprovedRunSubKey, entry.Name),
            AutorunScope.HklmRun => (RegistryHive.LocalMachine, ApprovedRunSubKey, entry.Name),
            AutorunScope.HklmWow64Run => (RegistryHive.LocalMachine, ApprovedRun32SubKey, entry.Name),
            AutorunScope.UserStartupFolder => (RegistryHive.CurrentUser, ApprovedStartupFolderSubKey, Path.GetFileName(entry.FilePath)),
            AutorunScope.CommonStartupFolder => (RegistryHive.LocalMachine, ApprovedStartupFolderSubKey, Path.GetFileName(entry.FilePath)),
            _ => (RegistryHive.CurrentUser, ApprovedRunSubKey, entry.Name)
        };
    }
}
