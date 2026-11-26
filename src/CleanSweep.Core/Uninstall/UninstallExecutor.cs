using System.Diagnostics;
using CleanSweep.Core.Models;

namespace CleanSweep.Core.Uninstall;

public static class UninstallExecutor
{
    public static async Task<OperationResult> RunNativeUninstallerAsync(
        InstalledApp app,
        bool preferQuiet,
        CancellationToken cancellationToken = default)
    {
        var result = new OperationResult();

        if (app.Source == AppSource.Msix && !string.IsNullOrWhiteSpace(app.PackageFullName))
        {
            return await RunMsixUninstallAsync(app.PackageFullName, cancellationToken);
        }

        string cmd = preferQuiet
            ? (!string.IsNullOrWhiteSpace(app.QuietUninstallString) ? app.QuietUninstallString :
               !string.IsNullOrWhiteSpace(app.SuggestedQuietCommand) ? app.SuggestedQuietCommand : app.UninstallString)
            : app.UninstallString;

        if (string.IsNullOrWhiteSpace(cmd))
        {
            result.Success = false;
            result.Summary = "Bu uygulama için kayıtlı bir kaldırma komutu bulunamadı.";
            result.Errors.Add(result.Summary);
            return result;
        }

        try
        {
            var (exe, args) = SplitCommand(cmd);
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = !preferQuiet,
                CreateNoWindow = preferQuiet
            };

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                result.Success = false;
                result.Summary = $"Kaldırma süreci başlatılamadı: {cmd}";
                result.Errors.Add(result.Summary);
                return result;
            }

            await proc.WaitForExitAsync(cancellationToken);
            result.Success = proc.ExitCode == 0 || proc.ExitCode == 3010; // 3010 = reboot required
            result.Summary = result.Success
                ? $"Yerleşik kaldırıcı tamamlandı (Çıkış kodu: {proc.ExitCode})."
                : $"Yerleşik kaldırıcı hata kodu ile sonlandı: {proc.ExitCode}.";
            if (!result.Success) result.Errors.Add(result.Summary);
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Summary = $"Kaldırma komutu çalıştırılamadı: {ex.Message}";
            result.Errors.Add(ex.Message);
            return result;
        }
    }

    private static async Task<OperationResult> RunMsixUninstallAsync(string packageFullName, CancellationToken ct)
    {
        var result = new OperationResult();
        try
        {
            string safePkg = packageFullName.Replace("'", "''");
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"Remove-AppxPackage -Package '{safePkg}' -ErrorAction Stop\"",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                result.Success = false;
                result.Summary = "PowerShell Remove-AppxPackage başlatılamadı.";
                result.Errors.Add(result.Summary);
                return result;
            }

            string err = await proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            result.Success = proc.ExitCode == 0;
            result.Summary = result.Success
                ? "Microsoft Store (MSIX) paketi başarıyla kaldırıldı."
                : $"MSIX paketi kaldırılamadı: {err.Trim()}";
            if (!result.Success && !string.IsNullOrWhiteSpace(err))
                result.Errors.Add(err.Trim());
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Summary = $"MSIX kaldırma hatası: {ex.Message}";
            result.Errors.Add(ex.Message);
            return result;
        }
    }

    public static async Task<bool> TryCreateSystemRestorePointAsync(string description)
    {
        try
        {
            string safeDesc = description.Replace("'", "");
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -Command \"Checkpoint-Computer -Description '{safeDesc}' -RestorePointType 'APPLICATION_UNINSTALL'\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static (string Executable, string Arguments) SplitCommand(string command)
    {
        string trimmed = command.Trim();
        if (trimmed.StartsWith('"'))
        {
            int endQuote = trimmed.IndexOf('"', 1);
            if (endQuote > 1)
            {
                string exe = trimmed[1..endQuote];
                string args = trimmed[(endQuote + 1)..].Trim();
                return (exe, args);
            }
        }

        int exeIdx = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIdx > 0)
        {
            string exe = trimmed[..(exeIdx + 4)];
            string args = trimmed[(exeIdx + 4)..].Trim();
            return (exe, args);
        }

        int firstSpace = trimmed.IndexOf(' ');
        if (firstSpace > 0)
        {
            return (trimmed[..firstSpace], trimmed[(firstSpace + 1)..].Trim());
        }

        return (trimmed, string.Empty);
    }
}
