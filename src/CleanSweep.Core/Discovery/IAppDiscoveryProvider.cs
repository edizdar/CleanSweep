using CleanSweep.Core.Models;

namespace CleanSweep.Core.Discovery;

public interface IAppDiscoveryProvider
{
    AppSource Source { get; }
    Task<IReadOnlyList<InstalledApp>> DiscoverAsync(CancellationToken cancellationToken = default);
}
