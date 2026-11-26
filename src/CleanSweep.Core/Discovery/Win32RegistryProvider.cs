using System.Text.RegularExpressions;
using CleanSweep.Core.Models;
using Microsoft.Win32;

namespace CleanSweep.Core.Discovery;

public sealed partial class Win32RegistryProvider : IAppDiscoveryProvider
{
    private const string UninstallSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public AppSource Source => AppSource.Win32;

    public Task<IReadOnlyList<InstalledApp>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<InstalledApp>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ReadHive(RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM", results, seenKeys, cancellationToken);
        ReadHive(RegistryHive.LocalMachine, RegistryView.Registry32, @"HKLM\WOW6432Node", results, seenKeys, cancellationToken);
        ReadHive(RegistryHive.CurrentUser, RegistryView.Registry64, "HKCU", results, seenKeys, cancellationToken);

        return Task.FromResult<IReadOnlyList<InstalledApp>>(results);
    }

    private static void ReadHive(
        RegistryHive hive,
        RegistryView view,
        string prefixLabel,
        List<InstalledApp> results,
        HashSet<string> seenKeys,
        CancellationToken ct)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstallKey = baseKey.OpenSubKey(UninstallSubKey, writable: false);
            if (uninstallKey == null) return;

            foreach (string subName in uninstallKey.GetSubKeyNames())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var appKey = uninstallKey.OpenSubKey(subName, writable: false);
                    if (appKey == null) continue;

                    string? displayName = appKey.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(displayName)) continue;

                    int sysComp = Convert.ToInt32(appKey.GetValue("SystemComponent") ?? 0);
                    string? releaseType = appKey.GetValue("ReleaseType") as string;
                    if (sysComp == 1 || !string.IsNullOrEmpty(releaseType)) continue;

                    string uninstallStr = appKey.GetValue("UninstallString") as string ?? string.Empty;
                    string quietUninstallStr = appKey.GetValue("QuietUninstallString") as string ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(uninstallStr) && string.IsNullOrWhiteSpace(quietUninstallStr))
                        continue;

                    string fullRegPath = view == RegistryView.Registry32 && hive == RegistryHive.LocalMachine
                        ? $@"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{subName}"
                        : $@"{(hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU")}\{UninstallSubKey}\{subName}";

                    if (!seenKeys.Add($"{prefixLabel}|{subName}|{displayName}"))
                        continue;

                    string publisher = (appKey.GetValue("Publisher") as string ?? string.Empty).Trim();
                    string version = (appKey.GetValue("DisplayVersion") as string ?? string.Empty).Trim();
                    string installLoc = NormalizeDir(appKey.GetValue("InstallLocation") as string);
                    string displayIcon = (appKey.GetValue("DisplayIcon") as string ?? string.Empty).Trim();
                    string installDate = (appKey.GetValue("InstallDate") as string ?? string.Empty).Trim();
                    bool isWindowsInstaller = Convert.ToInt32(appKey.GetValue("WindowsInstaller") ?? 0) == 1;

                    long sizeKb = 0;
                    object? rawSize = appKey.GetValue("EstimatedSize");
                    if (rawSize != null)
                    {
                        try { sizeKb = Convert.ToInt64(rawSize); } catch { }
                    }

                    if (string.IsNullOrEmpty(installLoc))
                    {
                        installLoc = InferInstallLocationFromStrings(uninstallStr, displayIcon);
                    }

                    var (installerType, suggestedQuiet) = DetectInstallerAndQuietCommand(
                        uninstallStr, quietUninstallStr, isWindowsInstaller, installLoc, subName);

                    results.Add(new InstalledApp
                    {
                        Id = $"win32:{prefixLabel}:{subName}",
                        Name = displayName.Trim(),
                        Publisher = publisher,
                        Version = version,
                        Source = AppSource.Win32,
                        InstallerType = installerType,
                        InstallLocation = installLoc,
                        UninstallString = uninstallStr,
                        QuietUninstallString = quietUninstallStr,
                        SuggestedQuietCommand = suggestedQuiet,
                        DisplayIcon = displayIcon,
                        RegistryKeyPath = fullRegPath,
                        EstimatedSizeBytes = sizeKb > 0 ? sizeKb * 1024L : 0,
                        InstallDate = installDate
                    });
                }
                catch
                {
                    // Skip unreadable entry
                }
            }
        }
        catch
        {
            // Skip inaccessible hive
        }
    }

    public static (InstallerType Type, string QuietCommand) DetectInstallerAndQuietCommand(
        string uninstallString,
        string quietUninstallString,
        bool isWindowsInstaller,
        string installLocation,
        string registryKeyName = "")
    {
        if (!string.IsNullOrWhiteSpace(quietUninstallString))
        {
            var typeFromQuiet = DetectTypeOnly(uninstallString, isWindowsInstaller, installLocation);
            return (typeFromQuiet, quietUninstallString);
        }

        var type = DetectTypeOnly(uninstallString, isWindowsInstaller, installLocation);
        string quiet = string.Empty;

        switch (type)
        {
            case InstallerType.Msi:
                var guidMatch = GuidRegex().Match(uninstallString + " " + registryKeyName);
                if (guidMatch.Success)
                    quiet = $"msiexec.exe /x {guidMatch.Value} /qn /norestart";
                break;

            case InstallerType.InnoSetup:
                string innoExe = ExtractExecutablePath(uninstallString);
                if (!string.IsNullOrEmpty(innoExe))
                    quiet = $"\"{innoExe}\" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART";
                break;

            case InstallerType.Nsis:
                string nsisExe = ExtractExecutablePath(uninstallString);
                if (!string.IsNullOrEmpty(nsisExe))
                    quiet = $"\"{nsisExe}\" /S";
                break;

            case InstallerType.Squirrel:
                string sqExe = ExtractExecutablePath(uninstallString);
                if (!string.IsNullOrEmpty(sqExe))
                    quiet = $"\"{sqExe}\" --uninstall -s";
                break;

            case InstallerType.WixBurn:
                string wixExe = ExtractExecutablePath(uninstallString);
                if (!string.IsNullOrEmpty(wixExe))
                    quiet = $"\"{wixExe}\" /uninstall /quiet /norestart";
                break;
        }

        return (type, quiet);
    }

    private static InstallerType DetectTypeOnly(string uninstallString, bool isWindowsInstaller, string installLocation)
    {
        string lower = uninstallString.ToLowerInvariant();
        if (isWindowsInstaller || lower.Contains("msiexec"))
            return InstallerType.Msi;
        if (lower.Contains("unins00") || lower.Contains("/verysilent"))
            return InstallerType.InnoSetup;
        if (lower.Contains("update.exe") && lower.Contains("--uninstall"))
            return InstallerType.Squirrel;
        if (lower.Contains("installshield") || lower.Contains("isuninst") || lower.Contains("setup.exe\" /remove"))
            return InstallerType.InstallShield;
        if (lower.Contains("package cache") || lower.Contains("/uninstall"))
            return InstallerType.WixBurn;

        if (!string.IsNullOrEmpty(installLocation))
        {
            try
            {
                if (File.Exists(Path.Combine(installLocation, "unins000.exe")))
                    return InstallerType.InnoSetup;
                if (File.Exists(Path.Combine(installLocation, "Update.exe")))
                    return InstallerType.Squirrel;
            }
            catch { }
        }

        if (lower.Contains("uninstall.exe") || lower.Contains("uninst.exe"))
            return InstallerType.Nsis;

        return InstallerType.Unknown;
    }

    public static string ExtractExecutablePath(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return string.Empty;
        string trimmed = command.Trim();
        if (trimmed.StartsWith('"'))
        {
            int endQuote = trimmed.IndexOf('"', 1);
            if (endQuote > 1) return trimmed[1..endQuote];
        }

        int exeIdx = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIdx > 0)
            return trimmed[..(exeIdx + 4)].Trim('"');

        int spaceIdx = trimmed.IndexOf(' ');
        return spaceIdx > 0 ? trimmed[..spaceIdx] : trimmed;
    }

    private static string InferInstallLocationFromStrings(string uninstallString, string displayIcon)
    {
        string exe = ExtractExecutablePath(uninstallString);
        if (!string.IsNullOrEmpty(exe) &&
            !exe.Contains("msiexec", StringComparison.OrdinalIgnoreCase) &&
            !exe.Contains("Package Cache", StringComparison.OrdinalIgnoreCase))
        {
            string? dir = Path.GetDirectoryName(exe);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                return dir;
        }

        if (!string.IsNullOrWhiteSpace(displayIcon))
        {
            string iconPath = displayIcon.Split(',')[0].Trim().Trim('"');
            if (iconPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                iconPath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
            {
                string? dir = Path.GetDirectoryName(iconPath);
                if (!string.IsNullOrEmpty(dir) &&
                    !dir.Contains(@"\Windows\", StringComparison.OrdinalIgnoreCase) &&
                    Directory.Exists(dir))
                {
                    return dir;
                }
            }
        }

        return string.Empty;
    }

    private static string NormalizeDir(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return string.Empty;
        return dir.Trim().Trim('"').TrimEnd('\\', '/');
    }

    [GeneratedRegex(@"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}")]
    private static partial Regex GuidRegex();
}
