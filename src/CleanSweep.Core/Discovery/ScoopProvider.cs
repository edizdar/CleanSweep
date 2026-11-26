using System.Text.Json;
using CleanSweep.Core.Models;

namespace CleanSweep.Core.Discovery;

public sealed class ScoopProvider : IAppDiscoveryProvider
{
    public AppSource Source => AppSource.Scoop;

    private sealed class ScoopManifest
    {
        public string? version { get; set; }
        public string? description { get; set; }
        public string? homepage { get; set; }
    }

    public Task<IReadOnlyList<InstalledApp>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<InstalledApp>();
        string scoopApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "apps");
        if (!Directory.Exists(scoopApps))
            return Task.FromResult<IReadOnlyList<InstalledApp>>(results);

        try
        {
            foreach (string appDir in Directory.EnumerateDirectories(scoopApps))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string appName = Path.GetFileName(appDir);
                if (appName.Equals("scoop", StringComparison.OrdinalIgnoreCase)) continue;

                string currentDir = Path.Combine(appDir, "current");
                string manifestPath = Path.Combine(currentDir, "manifest.json");
                if (!File.Exists(manifestPath)) continue;

                try
                {
                    string json = File.ReadAllText(manifestPath);
                    var manifest = JsonSerializer.Deserialize<ScoopManifest>(json);
                    results.Add(new InstalledApp
                    {
                        Id = $"scoop:{appName}",
                        Name = appName,
                        Publisher = "Scoop Package",
                        Version = manifest?.version ?? string.Empty,
                        Source = AppSource.Scoop,
                        InstallerType = InstallerType.Scoop,
                        InstallLocation = appDir,
                        UninstallString = $"scoop uninstall {appName}",
                        QuietUninstallString = $"scoop uninstall {appName}",
                        SuggestedQuietCommand = $"scoop uninstall {appName}"
                    });
                }
                catch { }
            }
        }
        catch { }

        return Task.FromResult<IReadOnlyList<InstalledApp>>(results);
    }
}
