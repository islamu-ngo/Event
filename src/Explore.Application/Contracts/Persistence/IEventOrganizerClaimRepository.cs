using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEventOrganizerClaimRepository : IGenericRepository<EventOrganizerClaim, Guid>
{
    Task<EventOrganizerClaim?> GetDetailsAsync(Guid id, bool trackChanges, CancellationToken cancellationToken);
    Task<EventOrganizerClaim?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);
    Task<EventOrganizerClaim?> GetByEventAndClaimantAsync(Guid eventId, Guid claimantActorId, bool trackChanges, CancellationToken cancellationToken);
    Task<IReadOnlyList<EventOrganizerClaim>> ListByEventAsync(Guid eventId, CancellationToken cancellationToken);
    Task<IReadOnlyList<EventOrganizerClaim>> ListByClaimantAsync(Guid claimantActorId, CancellationToken cancellationToken);
    Task UpdateApprovalAsync(EventOrganizerClaim claim, Event @event, CancellationToken cancellationToken);
}
