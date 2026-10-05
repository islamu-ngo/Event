using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Native terminal discovery fence. Callers own the transaction and must acquire
/// no new source authority locks after this fence.
/// </summary>
public interface IEventDiscoveryDisclosureRepository
{
    Task<EventDiscoveryRevision> AcquireCurrentAsync(
        Guid tenantId, CancellationToken cancellationToken);

    /// <summary>Advances a trusted, bounded set of affected tenants in one transaction.</summary>
    Task AdvanceAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken cancellationToken);
}
