using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEventLocationRepository
{
    const int MaximumBatchSize = 256;

    Task<EventLocation> AddAsync(EventLocation eventLocation, CancellationToken cancellationToken);
    Task<EventLocation?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<EventLocation>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<EventLocation>> GetByEventIdAsync(
        Guid eventId,
        CancellationToken cancellationToken);
    Task<EventLocation?> FindActivePhysicalAsync(
        Guid eventId,
        Guid locationId,
        CancellationToken cancellationToken);
    Task<EventLocation?> FindActiveToBeAnnouncedAsync(
        Guid eventId,
        CancellationToken cancellationToken);
    Task<bool> HasActiveCarrierReferencesAsync(
        Guid eventLocationId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<EventLocation>> GetActiveForGovernanceUpdateAsync(
        Guid? tenantId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Counts live EventLocations still flagged for privacy remediation across every tenant.
    /// Aggregate-only by design: readiness probes run without an ambient tenant and must never
    /// project venue rows.
    /// </summary>
    Task<int> CountNeedingPrivacyReviewAsync(CancellationToken cancellationToken);
    Task SaveGovernanceChangesAsync(
        IReadOnlyCollection<EventLocationDisclosureAudit> audits,
        IReadOnlyCollection<OutboxMessage> outboxMessages,
        CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
