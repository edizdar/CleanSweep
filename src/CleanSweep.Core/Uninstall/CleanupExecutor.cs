using System.Diagnostics;
using CleanSweep.Core.Models;
using CleanSweep.Core.Native;
using CleanSweep.Core.Quarantine;
using CleanSweep.Core.Safety;
using Microsoft.Win32;

namespace CleanSweep.Core.Uninstall;

public sealed class CleanupExecutor
{
    private readonly QuarantineManager _quarantineManager;

    public CleanupExecutor(QuarantineManager? quarantineManager = null)
    {
        _quarantineManager = quarantineManager ?? new QuarantineManager();
    }

    public OperationResult ExecuteCleanup(
        string appName,
        IReadOnlyList<LeftoverItem> selectedItems,
        CleanupOptions? options = null)
    {
        options ??= new CleanupOptions();
        var result = new OperationResult();

        var safeItems = selectedItems.Where(SafetyGuard.IsItemAllowed).ToList();
        var blockedItems = selectedItems.Where(i => !SafetyGuard.IsItemAllowed(i)).ToList();

        foreach (var blocked in blockedItems)
        {
            result.Errors.Add($"SafetyGuard tarafından engellendi: {blocked.DisplayPath}");
            result.Outcomes.Add(new ItemExecutionOutcome
            {
                Target = blocked.DisplayPath,
                Category = blocked.Category,
                Succeeded = false,
                Message = "Korunan sistem yolu olduğu için engellendi."
            });
        }

        if (safeItems.Count == 0)
        {
            result.Success = blockedItems.Count == 0;
            result.Summary = "Temizlenecek geçerli öğe seçilmedi.";
            return result;
        }

        if (options.DryRun)
        {
            foreach (var item in safeItems)
            {
                result.Outcomes.Add(new ItemExecutionOutcome
                {
                    Target = item.DisplayPath,
                    Category = item.Category,
                    Succeeded = true,
                    Message = "[Önizleme / Dry-Run] Silinmeye hazır."
                });
            }
            result.Success = true;
            result.Summary = $"[Dry-Run] {safeItems.Count} öğe temizlenmek üzere doğrulandı; sistemde değişiklik yapılmadı.";
            return result;
        }

        if (options.BackupToQuarantine)
        {
            try
            {
                var session = _quarantineManager.CreateSession(appName, safeItems);
                result.QuarantineSessionId = session.SessionId;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Summary = $"Karantina yedeği alınamadığı için silme işlemi güvenlik amacıyla durduruldu: {ex.Message}";
                result.Errors.Add(ex.Message);
                return result;
            }
        }

        // Stop services & terminate locking processes if requested (stubborn/forced uninstall)
        if (options.TerminateLockingProcesses)
        {
            StopSelectedServicesFirst(safeItems);
            TerminateAppAndLockingProcesses(safeItems, options.TargetInstallLocation);
        }

        // Execute Services first, then files/directories, then registry & artifacts
        var ordered = safeItems
            .OrderBy(i => i.Category == LeftoverCategory.Service ? 0 : 1)
            .ToList();

        foreach (var item in ordered)
        {
            var outcome = ExecuteSingleItem(item, options);
            result.Outcomes.Add(outcome);
            if (!outcome.Succeeded && !outcome.ScheduledForReboot)
            {
                result.Errors.Add($"{outcome.Target}: {outcome.Message}");
            }
        }

        int succeededCount = result.Outcomes.Count(o => o.Succeeded);
        int rebootCount = result.Outcomes.Count(o => o.ScheduledForReboot);
        result.Success = result.Errors.Count == 0;
        result.Summary = $"Temizlik tamamlandı: {succeededCount} öğe silindi" +
                         (rebootCount > 0 ? $", {rebootCount} öğe yeniden başlatmada silinmek üzere işaretlendi" : string.Empty) +
                         (result.Errors.Count > 0 ? $", {result.Errors.Count} hata" : string.Empty) + ".";
        return result;
    }

    private static void StopSelectedServicesFirst(IEnumerable<LeftoverItem> items)
    {
        foreach (var svc in items.Where(i => i.Category == LeftoverCategory.Service))
        {
            try { RunExternalTool("sc.exe", $"stop \"{svc.Path}\""); } catch { }
        }
    }

    private static void TerminateAppAndLockingProcesses(IEnumerable<LeftoverItem> items, string? targetInstallLocation)
    {
        var pidsToKill = new HashSet<int>();
        foreach (var item in items)
        {
            foreach (var lp in item.LockingProcesses)
            {
                if (lp.Pid != Environment.ProcessId && !SafetyGuard.IsProtectedProcessName(lp.ProcessName))
                    pidsToKill.Add(lp.Pid);
            }
        }

        string normLoc = !string.IsNullOrWhiteSpace(targetInstallLocation)
            ? SafetyGuard.NormalizeFilePath(targetInstallLocation)
            : string.Empty;

        if (!string.IsNullOrEmpty(normLoc))
        {
            try
            {
                foreach (var proc in Process.GetProcesses())
                {
                    try
                    {
                        if (proc.Id == Environment.ProcessId || SafetyGuard.IsProtectedProcessName(proc.ProcessName))
                            continue;

                        string? exePath = proc.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(exePath) &&
                            exePath.StartsWith(normLoc, StringComparison.OrdinalIgnoreCase))
                        {
                            pidsToKill.Add(proc.Id);
                        }
                    }
                    catch { }
                    finally { proc.Dispose(); }
                }
            }
            catch { }
        }

        bool killedAny = false;
        foreach (int pid in pidsToKill)
        {
            try
            {
                using var proc = Process.GetProcessById(pid);
                if (!SafetyGuard.IsProtectedProcessName(proc.ProcessName))
                {
                    proc.Kill(entireProcessTree: true);
                    killedAny = true;
                }
            }
            catch { }
        }

        if (killedAny)
            Thread.Sleep(400);
    }

    private static ItemExecutionOutcome ExecuteSingleItem(LeftoverItem item, CleanupOptions options)
    {
        try
        {
            switch (item.Category)
            {
                case LeftoverCategory.File:
                    return DeleteFileWithFallback(item.Path, options.ScheduleRebootOnLocked);

                case LeftoverCategory.Directory:
                    return DeleteDirectoryWithFallback(item.Path, options.ScheduleRebootOnLocked);

                case LeftoverCategory.RegistryKey:
                    DeleteRegistryKey(item.Path);
                    return new ItemExecutionOutcome
                    {
                        Target = item.DisplayPath,
                        Category = item.Category,
                        Succeeded = true,
                        Message = "Kayıt defteri anahtarı silindi."
                    };

                case LeftoverCategory.RegistryValue:
                case LeftoverCategory.FirewallRule:
                    DeleteRegistryValue(item.Path, item.ValueName!);
                    return new ItemExecutionOutcome
                    {
                        Target = item.DisplayPath,
                        Category = item.Category,
                        Succeeded = true,
                        Message = "Kayıt defteri/Güvenlik duvarı değeri silindi."
                    };

                case LeftoverCategory.Service:
                    RunExternalTool("sc.exe", $"stop \"{item.Path}\"");
                    int scCode = RunExternalTool("sc.exe", $"delete \"{item.Path}\"");
                    return new ItemExecutionOutcome
                    {
                        Target = item.Path,
                        Category = item.Category,
                        Succeeded = scCode == 0,
                        Message = scCode == 0 ? "Windows servisi kaldırıldı." : $"sc delete çıkış kodu: {scCode}"
                    };

                case LeftoverCategory.ScheduledTask:
                    int taskCode = RunExternalTool("schtasks.exe", $"/Delete /TN \"{item.Path}\" /F");
                    return new ItemExecutionOutcome
                    {
                        Target = item.Path,
                        Category = item.Category,
                        Succeeded = taskCode == 0,
                        Message = taskCode == 0 ? "Zamanlanmış görev silindi." : $"schtasks çıkış kodu: {taskCode}"
                    };

                case LeftoverCategory.EnvironmentPath:
                    RemoveFromPath(item.Path, item.ValueName ?? @"HKCU\Environment");
                    return new ItemExecutionOutcome
                    {
                        Target = item.Path,
                        Category = item.Category,
                        Succeeded = true,
                        Message = "PATH ortam değişkeninden kaldırıldı."
                    };

                default:
                    return new ItemExecutionOutcome
                    {
                        Target = item.DisplayPath,
                        Category = item.Category,
                        Succeeded = false,
                        Message = "Desteklenmeyen kategori."
                    };
            }
        }
        catch (Exception ex)
        {
            return new ItemExecutionOutcome
            {
                Target = item.DisplayPath,
                Category = item.Category,
                Succeeded = false,
                Message = ex.Message
            };
        }
    }

    private static ItemExecutionOutcome DeleteFileWithFallback(string path, bool scheduleReboot)
    {
        if (!File.Exists(path))
        {
            return new ItemExecutionOutcome
            {
                Target = path,
                Category = LeftoverCategory.File,
                Succeeded = true,
                Message = "Dosya zaten mevcut değil."
            };
        }

        try
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
            return new ItemExecutionOutcome
            {
                Target = path,
                Category = LeftoverCategory.File,
                Succeeded = true,
                Message = "Dosya silindi."
            };
        }
        catch (Exception ex) when (scheduleReboot && (ex is IOException || ex is UnauthorizedAccessException))
        {
            bool scheduled = NativeFileOps.ScheduleDeleteOnReboot(path);
            return new ItemExecutionOutcome
            {
                Target = path,
                Category = LeftoverCategory.File,
                Succeeded = false,
                ScheduledForReboot = scheduled,
                Message = scheduled
                    ? "Dosya kilitli; yeniden başlatmada silinmek üzere işaretlendi."
                    : $"Dosya silinemedi: {ex.Message}"
            };
        }
    }

    private static ItemExecutionOutcome DeleteDirectoryWithFallback(string path, bool scheduleReboot)
    {
        if (!Directory.Exists(path))
        {
            return new ItemExecutionOutcome
            {
                Target = path,
                Category = LeftoverCategory.Directory,
                Succeeded = true,
                Message = "Klasör zaten mevcut değil."
            };
        }

        try
        {
            ClearStubbornAttributesRecursive(path);
            Directory.Delete(path, recursive: true);
            return new ItemExecutionOutcome
            {
                Target = path,
                Category = LeftoverCategory.Directory,
                Succeeded = true,
                Message = "Klasör tamamen silindi."
            };
        }
        catch (Exception ex) when (scheduleReboot && (ex is IOException || ex is UnauthorizedAccessException))
        {
            bool anyScheduled = false;
            try
            {
                foreach (string f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.SetAttributes(f, FileAttributes.Normal);
                        File.Delete(f);
                    }
                    catch { anyScheduled |= NativeFileOps.ScheduleDeleteOnReboot(f); }
                }
                anyScheduled |= NativeFileOps.ScheduleDeleteOnReboot(path);
            }
            catch { }

            return new ItemExecutionOutcome
            {
                Target = path,
                Category = LeftoverCategory.Directory,
                Succeeded = false,
                ScheduledForReboot = anyScheduled,
                Message = anyScheduled
                    ? "Klasörde kilitli dosyalar var; yeniden başlatmada silinmek üzere işaretlendi."
                    : $"Klasör silinemedi: {ex.Message}"
            };
        }
    }

    private static void ClearStubbornAttributesRecursive(string dirPath)
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
            }
            foreach (string dir in Directory.EnumerateDirectories(dirPath, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(dir, FileAttributes.Normal); } catch { }
            }
            File.SetAttributes(dirPath, FileAttributes.Normal);
        }
        catch { }
    }

    private static void DeleteRegistryKey(string fullPath)
    {
        if (!RegistryExportHelper.TryParseRegistryPath(fullPath, out var hive, out string subKeyPath, out _))
            throw new InvalidOperationException($"Geçersiz kayıt defteri yolu: {fullPath}");

        using var base64 = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        base64.DeleteSubKeyTree(subKeyPath, throwOnMissingSubKey: false);

        using var base32 = RegistryKey.OpenBaseKey(hive, RegistryView.Registry32);
        base32.DeleteSubKeyTree(subKeyPath, throwOnMissingSubKey: false);
    }

    private static void DeleteRegistryValue(string fullPath, string valueName)
    {
        if (!RegistryExportHelper.TryParseRegistryPath(fullPath, out var hive, out string subKeyPath, out _))
            throw new InvalidOperationException($"Geçersiz kayıt defteri yolu: {fullPath}");

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var subKey = baseKey.OpenSubKey(subKeyPath, writable: true);
            subKey?.DeleteValue(valueName, throwOnMissingValue: false);
        }
    }

    private static void RemoveFromPath(string targetSegment, string scopeHint)
    {
        var target = scopeHint.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase)
            ? EnvironmentVariableTarget.Machine
            : EnvironmentVariableTarget.User;

        string? current = Environment.GetEnvironmentVariable("PATH", target);
        if (string.IsNullOrWhiteSpace(current)) return;

        var kept = current
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !SafetyGuard.NormalizeFilePath(s).Equals(SafetyGuard.NormalizeFilePath(targetSegment), StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Environment.SetEnvironmentVariable("PATH", string.Join(";", kept), target);
    }

    private static int RunExternalTool(string fileName, string args)
    {
        using var proc = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true
        });
        proc?.WaitForExit(10_000);
        return proc?.ExitCode ?? -1;
    }
}
