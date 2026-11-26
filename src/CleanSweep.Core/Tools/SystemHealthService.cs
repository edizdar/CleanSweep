using System.Diagnostics;
using System.Runtime.InteropServices;
using CleanSweep.Core.Models;
using CleanSweep.Core.Native;

namespace CleanSweep.Core.Tools;

public sealed class SystemDiskStatus
{
    public string DriveName { get; init; } = "C:\\";
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }
    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);
    public double UsedPercent => TotalBytes > 0 ? Math.Round(UsedBytes * 100.0 / TotalBytes, 1) : 0;
    public string FormattedFree => InstalledApp.FormatBytes(FreeBytes);
    public string FormattedTotal => InstalledApp.FormatBytes(TotalBytes);
    public string OsDescription { get; init; } = string.Empty;

    public string SummaryText => TotalBytes > 0
        ? $"{DriveName} — Boş: {FormattedFree} / {FormattedTotal} (%{UsedPercent:0} dolu)"
        : $"{DriveName} — Disk bilgisi alınamadı";
}

public static class SystemHealthService
{
    public static SystemDiskStatus GetSystemDiskStatus()
    {
        string osDesc = RuntimeInformation.OSDescription;
        string root = "C:\\";
        try
        {
            string sysRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            if (!string.IsNullOrWhiteSpace(sysRoot))
                root = sysRoot;

            var drive = new DriveInfo(root);
            if (drive.IsReady)
            {
                return new SystemDiskStatus
                {
                    DriveName = drive.Name,
                    TotalBytes = drive.TotalSize,
                    FreeBytes = drive.AvailableFreeSpace,
                    OsDescription = osDesc
                };
            }
        }
        catch
        {
        }

        return new SystemDiskStatus
        {
            DriveName = root,
            TotalBytes = 0,
            FreeBytes = 0,
            OsDescription = osDesc
        };
    }

    public static async Task<(bool Success, string Message)> CreateSystemRestorePointAsync(
        string description = "CleanSweep Yedek Noktası",
        CancellationToken ct = default)
    {
        if (!NativeFileOps.IsRunningAsAdministrator())
        {
            return (false, "Sistem Geri Yükleme Noktası oluşturmak için CleanSweep'i Yönetici olarak başlatın.");
        }

        string safeDesc = (description ?? "CleanSweep Yedek Noktası")
            .Replace('\'', ' ')
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        if (string.IsNullOrEmpty(safeDesc))
            safeDesc = "CleanSweep Yedek Noktası";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"Checkpoint-Computer -Description '{safeDesc}' -RestorePointType 'MODIFY_SETTINGS'\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null)
                return (false, "PowerShell başlatılamadı.");

            string stdout = await proc.StandardOutput.ReadToEndAsync(ct);
            string stderr = await proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            if (proc.ExitCode == 0)
            {
                return (true, $"Sistem Geri Yükleme Noktası başarıyla oluşturuldu: \"{safeDesc}\".");
            }

            string err = !string.IsNullOrWhiteSpace(stderr) ? stderr.Trim() : stdout.Trim();
            if (err.Contains("frequency", StringComparison.OrdinalIgnoreCase) || err.Contains("1440", StringComparison.OrdinalIgnoreCase))
                return (false, "Windows yakın zamanda zaten bir geri yükleme noktası oluşturdu (24 saat sınırı).");
            if (err.Contains("disabled", StringComparison.OrdinalIgnoreCase) || err.Contains("0x80042306", StringComparison.OrdinalIgnoreCase))
                return (false, "Sistem Koruması (Geri Yükleme) bu sürücüde kapalı olabilir. Windows Araçları > Sistem Koruması'ndan açabilirsiniz.");

            return (false, string.IsNullOrWhiteSpace(err)
                ? $"Geri yükleme noktası oluşturulamadı (çıkış kodu: {proc.ExitCode})."
                : $"Geri yükleme noktası oluşturulamadı: {err}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (false, $"Sistem Geri Yükleme Noktası oluşturulurken hata: {ex.Message}");
        }
    }
}
