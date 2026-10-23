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
    /// Saves paired disclosure audits and projection-correction messages and retains their
    /// exact correction receipts in the caller's transaction. Message-bearing saves require
    /// an existing transaction so disclosure, audit, outbox, and receipt commit together.
    /// </summary>
    Task SaveGovernanceChangesAsync(
        IReadOnlyCollection<EventLocationDisclosureAudit> audits,
        IReadOnlyCollection<OutboxMessage> outboxMessages,
        CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
