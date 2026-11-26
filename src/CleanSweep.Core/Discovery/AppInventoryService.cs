using CleanSweep.Core.Models;

namespace CleanSweep.Core.Discovery;

public sealed class AppInventoryService
{
    private readonly IReadOnlyList<IAppDiscoveryProvider> _providers;

    public AppInventoryService()
        : this([
            new Win32RegistryProvider(),
            new AppxProvider(),
            new ScoopProvider(),
            new ChocolateyProvider()
        ])
    {
    }

    public AppInventoryService(IEnumerable<IAppDiscoveryProvider> providers)
    {
        _providers = providers.ToList();
    }

    public async Task<IReadOnlyList<InstalledApp>> GetInstalledAppsAsync(
        AppSource? filterSource = null,
        CancellationToken cancellationToken = default)
    {
        var activeProviders = filterSource.HasValue
            ? _providers.Where(p => p.Source == filterSource.Value).ToList()
            : _providers;

        var tasks = activeProviders.Select(p => p.DiscoverAsync(cancellationToken));
        var providerResults = await Task.WhenAll(tasks);

        return providerResults
            .SelectMany(r => r)
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
