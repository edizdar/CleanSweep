using System.Xml.Linq;
using CleanSweep.Core.Models;

namespace CleanSweep.Core.Discovery;

public sealed class ChocolateyProvider : IAppDiscoveryProvider
{
    public AppSource Source => AppSource.Chocolatey;

    public Task<IReadOnlyList<InstalledApp>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<InstalledApp>();
        string chocoLib = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "chocolatey", "lib");
        if (!Directory.Exists(chocoLib))
            return Task.FromResult<IReadOnlyList<InstalledApp>>(results);

        try
        {
            foreach (string pkgDir in Directory.EnumerateDirectories(chocoLib))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string pkgName = Path.GetFileName(pkgDir);
                string? nuspec = Directory.EnumerateFiles(pkgDir, "*.nuspec").FirstOrDefault();
                if (nuspec == null) continue;

                try
                {
                    var doc = XDocument.Load(nuspec);
                    var meta = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "metadata");
                    string id = meta?.Elements().FirstOrDefault(e => e.Name.LocalName == "id")?.Value ?? pkgName;
                    string title = meta?.Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value ?? id;
                    string version = meta?.Elements().FirstOrDefault(e => e.Name.LocalName == "version")?.Value ?? string.Empty;
                    string authors = meta?.Elements().FirstOrDefault(e => e.Name.LocalName == "authors")?.Value ?? "Chocolatey";

                    results.Add(new InstalledApp
                    {
                        Id = $"choco:{id}",
                        Name = string.IsNullOrWhiteSpace(title) ? id : title,
                        Publisher = authors,
                        Version = version,
                        Source = AppSource.Chocolatey,
                        InstallerType = InstallerType.Chocolatey,
                        InstallLocation = pkgDir,
                        PackageFullName = id,
                        UninstallString = $"choco uninstall {id} -y",
                        QuietUninstallString = $"choco uninstall {id} -y",
                        SuggestedQuietCommand = $"choco uninstall {id} -y"
                    });
                }
                catch { }
            }
        }
        catch { }

        return Task.FromResult<IReadOnlyList<InstalledApp>>(results);
    }
}
