using System.Diagnostics;
using System.Xml.Linq;
using CleanSweep.Core.Discovery;
using CleanSweep.Core.Models;
using Microsoft.Win32;

namespace CleanSweep.Core.Startup;

public sealed class AutorunService
{
    private const string RunSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunOnceSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string PoliciesRunSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run";
    private const string ApprovedRunSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ApprovedRun32SubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";
    private const string ApprovedStartupFolderSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";
    private const string MsixStartupSubKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData";

    public IReadOnlyList<AutorunEntry> GetEntries()
    {
        var entries = new List<AutorunEntry>();

        ReadRunKey(RegistryHive.CurrentUser, RegistryView.Registry64, RunSubKey, @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            "Kullanıcı Kayıt Defteri (HKCU\\Run)", AutorunScope.HkcuRun, ApprovedRunSubKey, entries);

        ReadRunKey(RegistryHive.LocalMachine, RegistryView.Registry64, RunSubKey, @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            "Sistem Kayıt Defteri (HKLM\\Run)", AutorunScope.HklmRun, ApprovedRunSubKey, entries);

        ReadRunKey(RegistryHive.LocalMachine, RegistryView.Registry32, RunSubKey, @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run",
            "Sistem 32-bit (WOW6432Node\\Run)", AutorunScope.HklmWow64Run, ApprovedRun32SubKey, entries);

        ReadRunKey(RegistryHive.CurrentUser, RegistryView.Registry64, RunOnceSubKey, @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
            "Tek Seferlik Başlangıç (HKCU\\RunOnce)", AutorunScope.HkcuRunOnce, ApprovedRunSubKey, entries);

        ReadRunKey(RegistryHive.LocalMachine, RegistryView.Registry64, RunOnceSubKey, @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
            "Tek Seferlik Başlangıç (HKLM\\RunOnce)", AutorunScope.HklmRunOnce, ApprovedRunSubKey, entries);

        ReadRunKey(RegistryHive.CurrentUser, RegistryView.Registry64, PoliciesRunSubKey, @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run",
            "İlke Başlangıcı (HKCU\\Policies\\Run)", AutorunScope.PoliciesRun, ApprovedRunSubKey, entries);

        ReadRunKey(RegistryHive.LocalMachine, RegistryView.Registry64, PoliciesRunSubKey, @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run",
            "İlke Başlangıcı (HKLM\\Policies\\Run)", AutorunScope.PoliciesRun, ApprovedRunSubKey, entries);

        ReadStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            RegistryHive.CurrentUser, "Kullanıcı Başlangıç Klasörü", AutorunScope.UserStartupFolder, entries);

        ReadStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
            RegistryHive.LocalMachine, "Ortak Başlangıç Klasörü", AutorunScope.CommonStartupFolder, entries);

        ReadMsixStartupTasks(entries);
        ReadLogonScheduledTasks(entries);

        return entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool IsApprovedEnabled(byte[]? rawData)
    {
        if (rawData == null || rawData.Length == 0) return true;
        return rawData[0] is 0x02 or 0x06;
    }

    public static bool IsMsixStartupStateEnabled(int state) => state is 2 or 4;

    public bool SetEnabled(AutorunEntry entry, bool enabled)
    {
        try
        {
            if (entry.Scope == AutorunScope.MsixStartupTask)
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
                using var taskKey = baseKey.OpenSubKey(entry.TaskSubKeyOrPath, writable: true);
                if (taskKey == null) return false;
                taskKey.SetValue("State", enabled ? 2 : 1, RegistryValueKind.DWord);
                entry.IsEnabled = enabled;
                return true;
            }

            if (entry.Scope == AutorunScope.ScheduledTask)
            {
                string flag = enabled ? "/ENABLE" : "/DISABLE";
                int code = RunSchtasks($"/Change /TN \"{entry.TaskSubKeyOrPath}\" {flag}");
                if (code == 0)
                {
                    entry.IsEnabled = enabled;
                    return true;
                }
                return false;
            }

            var (hive, approvedSubKey, valueName) = ResolveApprovalTarget(entry);
            using var bKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = bKey.CreateSubKey(approvedSubKey, writable: true);
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
            if (entry.Scope == AutorunScope.ScheduledTask)
            {
                return RunSchtasks($"/Delete /TN \"{entry.TaskSubKeyOrPath}\" /F") == 0;
            }

            if (entry.Scope == AutorunScope.MsixStartupTask)
            {
                // Disable MSIX startup task safely instead of breaking package registration
                return SetEnabled(entry, false);
            }

            if (entry.Scope is AutorunScope.UserStartupFolder or AutorunScope.CommonStartupFolder)
            {
                if (File.Exists(entry.FilePath))
                {
                    File.Delete(entry.FilePath);
                    return true;
                }
                return false;
            }

            var (hive, view, subKey) = entry.Scope switch
            {
                AutorunScope.HkcuRun => (RegistryHive.CurrentUser, RegistryView.Registry64, RunSubKey),
                AutorunScope.HklmWow64Run => (RegistryHive.LocalMachine, RegistryView.Registry32, RunSubKey),
                AutorunScope.HkcuRunOnce => (RegistryHive.CurrentUser, RegistryView.Registry64, RunOnceSubKey),
                AutorunScope.HklmRunOnce => (RegistryHive.LocalMachine, RegistryView.Registry64, RunOnceSubKey),
                AutorunScope.PoliciesRun => (entry.RegistryPath.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase) ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, RegistryView.Registry64, PoliciesRunSubKey),
                _ => (RegistryHive.LocalMachine, RegistryView.Registry64, RunSubKey)
            };

            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var runKey = baseKey.OpenSubKey(subKey, writable: true);
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
        string subKeyPath,
        string fullRegPath,
        string locationLabel,
        AutorunScope scope,
        string approvedSubKey,
        List<AutorunEntry> entries)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var runKey = baseKey.OpenSubKey(subKeyPath, writable: false);
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

    private static void ReadMsixStartupTasks(List<AutorunEntry> entries)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            using var appDataRoot = baseKey.OpenSubKey(MsixStartupSubKey, writable: false);
            if (appDataRoot == null) return;

            foreach (string pkgFamily in appDataRoot.GetSubKeyNames())
            {
                try
                {
                    using var pkgKey = appDataRoot.OpenSubKey(pkgFamily, writable: false);
                    if (pkgKey == null) continue;

                    foreach (string taskName in pkgKey.GetSubKeyNames())
                    {
                        try
                        {
                            using var taskKey = pkgKey.OpenSubKey(taskName, writable: false);
                            object? stateObj = taskKey?.GetValue("State");
                            if (stateObj == null) continue;

                            int state = Convert.ToInt32(stateObj);
                            string appLabel = pkgFamily.Split('_')[0];
                            if (appLabel.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase))
                                appLabel = appLabel["Microsoft.".Length..];

                            string subKeyPath = $@"{MsixStartupSubKey}\{pkgFamily}\{taskName}";
                            entries.Add(new AutorunEntry
                            {
                                Id = $"Msix:{pkgFamily}:{taskName}",
                                Name = $"{appLabel} ({taskName})",
                                Command = $"Windows Store Görevi: {pkgFamily}",
                                Location = "Windows Uygulaması (MSIX Startup)",
                                Scope = AutorunScope.MsixStartupTask,
                                IsEnabled = IsMsixStartupStateEnabled(state),
                                TaskSubKeyOrPath = subKeyPath,
                                RegistryPath = $@"HKCU\{subKeyPath}"
                            });
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    private static void ReadLogonScheduledTasks(List<AutorunEntry> entries)
    {
        string tasksDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "Tasks");
        if (!Directory.Exists(tasksDir)) return;

        try
        {
            foreach (string file in Directory.EnumerateFiles(tasksDir, "*", SearchOption.AllDirectories))
            {
                string relative = "\\" + Path.GetRelativePath(tasksDir, file);
                if (relative.StartsWith(@"\Microsoft\Windows\", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var doc = XDocument.Load(file);
                    bool isStartupTrigger = doc.Descendants().Any(e =>
                        e.Name.LocalName is "LogonTrigger" or "BootTrigger");
                    if (!isStartupTrigger) continue;

                    string? enabledStr = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Enabled")?.Value;
                    bool isEnabled = !string.Equals(enabledStr, "false", StringComparison.OrdinalIgnoreCase);

                    string command = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Command")?.Value ?? relative;
                    string args = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Arguments")?.Value ?? string.Empty;
                    string fullCmd = string.IsNullOrWhiteSpace(args) ? command : $"{command} {args}";
                    string taskName = Path.GetFileName(file);

                    entries.Add(new AutorunEntry
                    {
                        Id = $"Task:{relative}",
                        Name = taskName,
                        Command = fullCmd,
                        ExecutablePath = Win32RegistryProvider.ExtractExecutablePath(command),
                        Location = "Zamanlanmış Başlangıç Görevi",
                        Scope = AutorunScope.ScheduledTask,
                        IsEnabled = isEnabled,
                        TaskSubKeyOrPath = relative
                    });
                }
                catch { }
            }
        }
        catch { }
    }

    private static (RegistryHive Hive, string ApprovedSubKey, string ValueName) ResolveApprovalTarget(AutorunEntry entry)
    {
        return entry.Scope switch
        {
            AutorunScope.HkcuRun or AutorunScope.HkcuRunOnce => (RegistryHive.CurrentUser, ApprovedRunSubKey, entry.Name),
            AutorunScope.HklmRun or AutorunScope.HklmRunOnce => (RegistryHive.LocalMachine, ApprovedRunSubKey, entry.Name),
            AutorunScope.HklmWow64Run => (RegistryHive.LocalMachine, ApprovedRun32SubKey, entry.Name),
            AutorunScope.PoliciesRun => (entry.RegistryPath.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase) ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, ApprovedRunSubKey, entry.Name),
            AutorunScope.UserStartupFolder => (RegistryHive.CurrentUser, ApprovedStartupFolderSubKey, Path.GetFileName(entry.FilePath)),
            AutorunScope.CommonStartupFolder => (RegistryHive.LocalMachine, ApprovedStartupFolderSubKey, Path.GetFileName(entry.FilePath)),
            _ => (RegistryHive.CurrentUser, ApprovedRunSubKey, entry.Name)
        };
    }

    private static int RunSchtasks(string args)
    {
        using var proc = Process.Start(new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true
        });
        proc?.WaitForExit(10_000);
        return proc?.ExitCode ?? -1;
    }
}
