using Explore.Domain;
using Explore.Domain.Services.Discovery;

namespace Explore.Application.Contracts.Persistence;

public interface IEventDiscoverySnapshotRepository
{
    /// <summary>
    /// Acquires the dedicated native reservation row in the caller's Serializable transaction,
    /// before candidate materialization. Order: reservation, candidate/source work, terminal epoch, snapshot rows.
    /// Public writers never acquire reservation; acquire no new source locks after the terminal epoch.
    /// </summary>
    Task AcquireFenceAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns matching live metadata without loading membership.
    /// The caller supplies current epochs from its consistent authority boundary and reads items through GetItemsAsync.
    /// </summary>
    Task<EventDiscoverySnapshot?> FindReusableAsync(
        Guid tenantId, string criteriaHash, long identityEpoch, long disclosureEpoch,
        DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Reuses matching live membership or atomically checks capacity and inserts the whole snapshot.
    /// Invoke after the caller establishes its terminal epoch fence and captured authority.
    /// Returns null when capacity is exhausted; never commits the caller's transaction.
    /// </summary>
    Task<EventDiscoverySnapshot?> CaptureAsync(
        EventDiscoverySnapshot snapshot, EventDiscoveryTraversalLimits limits,
        DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>Returns stored metadata only. The caller checks expiry and current epochs before disclosure.</summary>
    Task<EventDiscoverySnapshot?> GetAsync(Guid tenantId, Guid snapshotId, CancellationToken cancellationToken);

    /// <summary>Seeks strictly after the zero-based ordinal; use -1 for the first page, with take from 1 to 100.</summary>
    Task<IReadOnlyList<EventDiscoverySnapshotItem>> GetItemsAsync(
        Guid tenantId, Guid snapshotId, long afterOrdinal, int take, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes at most 100 expired snapshots and their bounded membership.
    /// The caller acquires reservation then the terminal epoch before invoking this primitive, as for capture.
    /// </summary>
    Task<int> PurgeExpiredAsync(
        Guid tenantId, DateTime nowUtc, int maxSnapshots, CancellationToken cancellationToken);
}
