namespace CleanSweep.Core.Models;

public enum AppSource
{
    Win32,
    Msix,
    Scoop,
    Chocolatey
}

public enum InstallerType
{
    Unknown,
    Msi,
    InnoSetup,
    Nsis,
    InstallShield,
    Squirrel,
    WixBurn,
    Msix,
    Scoop,
    Chocolatey
}

public sealed class InstalledApp
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Publisher { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public AppSource Source { get; init; }
    public InstallerType InstallerType { get; init; }
    public string InstallLocation { get; init; } = string.Empty;
    public string UninstallString { get; init; } = string.Empty;
    public string QuietUninstallString { get; init; } = string.Empty;
    public string SuggestedQuietCommand { get; init; } = string.Empty;
    public string DisplayIcon { get; init; } = string.Empty;
    public string RegistryKeyPath { get; init; } = string.Empty;
    public string PackageFullName { get; init; } = string.Empty;
    public string PackageFamilyName { get; init; } = string.Empty;
    public long EstimatedSizeBytes { get; init; }
    public string InstallDate { get; init; } = string.Empty;
    public bool IsSystemComponent { get; init; }

    public string FormattedSize => FormatBytes(EstimatedSizeBytes);

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "-";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.##} {units[unit]}";
    }
}
