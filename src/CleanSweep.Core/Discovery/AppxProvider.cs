using System.Diagnostics;
using System.Text.Json;
using CleanSweep.Core.Models;

namespace CleanSweep.Core.Discovery;

public sealed class AppxProvider : IAppDiscoveryProvider
{
    public AppSource Source => AppSource.Msix;

    private sealed class AppxDto
    {
        public string? Name { get; set; }
        public string? PackageFullName { get; set; }
        public string? PackageFamilyName { get; set; }
        public string? Publisher { get; set; }
        public string? Version { get; set; }
        public string? InstallLocation { get; set; }
    }

    public async Task<IReadOnlyList<InstalledApp>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<InstalledApp>();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"Get-AppxPackage | Where-Object { -not $_.IsFramework -and -not $_.NonRemovable -and $_.SignatureKind -ne 'System' } | Select-Object Name,PackageFullName,PackageFamilyName,Publisher,Version,InstallLocation | ConvertTo-Json -Compress\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return list;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(12));

            string json = await proc.StandardOutput.ReadToEndAsync(cts.Token);
            await proc.WaitForExitAsync(cts.Token);

            if (string.IsNullOrWhiteSpace(json)) return list;
            json = json.Trim();
            if (json.StartsWith('{')) json = $"[{json}]";

            var items = JsonSerializer.Deserialize<List<AppxDto>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (items == null) return list;

            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.PackageFullName))
                    continue;

                string publisher = ExtractCn(item.Publisher ?? string.Empty);
                if (Guid.TryParse(publisher, out _))
                    publisher = "Microsoft Store";

                list.Add(new InstalledApp
                {
                    Id = $"msix:{item.PackageFullName}",
                    Name = item.Name,
                    Publisher = publisher,
                    Version = item.Version ?? string.Empty,
                    Source = AppSource.Msix,
                    InstallerType = InstallerType.Msix,
                    InstallLocation = item.InstallLocation ?? string.Empty,
                    PackageFullName = item.PackageFullName,
                    PackageFamilyName = item.PackageFamilyName ?? string.Empty,
                    UninstallString = $"powershell.exe -NoProfile -Command \"Remove-AppxPackage -Package '{item.PackageFullName}'\"",
                    QuietUninstallString = $"powershell.exe -NoProfile -Command \"Remove-AppxPackage -Package '{item.PackageFullName}'\"",
                    SuggestedQuietCommand = $"powershell.exe -NoProfile -Command \"Remove-AppxPackage -Package '{item.PackageFullName}'\""
                });
            }
        }
        catch
        {
            // Gracefully return empty if PowerShell/Appx query fails or times out
        }

        return list;
    }

    private static string ExtractCn(string distinguishedName)
    {
        if (string.IsNullOrWhiteSpace(distinguishedName)) return string.Empty;
        foreach (string part in distinguishedName.Split(','))
        {
            string trimmed = part.Trim();
            if (trimmed.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                return trimmed[3..].Trim('"');
        }
        return distinguishedName;
    }
}
