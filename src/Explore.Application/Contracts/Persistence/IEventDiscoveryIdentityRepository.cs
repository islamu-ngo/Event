using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEventDiscoveryIdentityRepository
{
    /// <summary>
    /// Requires a caller-owned serializable unit of work. Acquire domain/authority locks first,
    /// then this sorted identity fence and terminal tenant epoch fence. Acquire no domain locks afterward.
    /// Reload both groups and reauthorize current roots under this fence before deciding.
    /// The complete operation, including authority, audit and outbox, belongs inside the execution strategy.
    /// </summary>
    Task AcquireFenceAsync(Guid tenantId, IReadOnlyCollection<Guid> identityIds, CancellationToken cancellationToken);

    Task<EventDiscoveryIdentity?> FindAsync(
        Guid tenantId, EventDiscoverySourceKind sourceKind, string sourceKey, CancellationToken cancellationToken);

    /// <summary>Reads at most 1000 exact bindings for an already bounded discovery window.</summary>
    Task<IReadOnlyList<EventDiscoveryIdentity>> GetBindingsAsync(
        Guid tenantId, EventDiscoverySourceKind sourceKind,
        IReadOnlyCollection<string> sourceKeys, CancellationToken cancellationToken);

    /// <summary>Requires the fence; a tombstoned source binding is never recreated.</summary>
    Task<EventDiscoveryIdentity> GetOrCreateAsync(
        Guid tenantId, EventDiscoverySourceKind sourceKind, string sourceKey, CancellationToken cancellationToken);

    Task<EventDiscoveryRevision?> GetRevisionAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<IReadOnlyList<EventDiscoveryIdentity>> GetGroupAsync(
        Guid tenantId, Guid identityId, CancellationToken cancellationToken);

    /// <summary>Persists the graph inside the caller's transaction, but never commits it.</summary>
    Task<EventDiscoveryRevision> ReviewAsync(
        Guid tenantId, Guid memberId, Guid primaryId, long expectedRevision,
        Guid reviewerId, string reasonCode, DateTime reviewedAtUtc, CancellationToken cancellationToken);

    Task<EventDiscoveryRevision> ReverseAsync(
        Guid tenantId, Guid memberId, Guid primaryId, long expectedRevision,
        Guid reviewerId, string reasonCode, DateTime reviewedAtUtc, CancellationToken cancellationToken);

    /// <summary>Requires the terminal fence and persists the epoch in the caller's transaction.</summary>
    Task<EventDiscoveryRevision> AdvanceDisclosureAsync(Guid tenantId, CancellationToken cancellationToken);
}
