using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class EventOrganizerClaimRepository(ExploreDbContext dbContext)
    : GenericRepository<EventOrganizerClaim, Guid>(dbContext), IEventOrganizerClaimRepository
{
    public Task<EventOrganizerClaim?> GetDetailsAsync(Guid id, bool trackChanges, CancellationToken cancellationToken)
    {
        return DetailsQuery(trackChanges)
            .FirstOrDefaultAsync(claim => claim.Id == id, cancellationToken);
    }

    public Task<EventOrganizerClaim?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        return DetailsQuery(trackChanges: true)
            .FirstOrDefaultAsync(claim => claim.Id == id, cancellationToken);
    }

    public Task<EventOrganizerClaim?> GetByEventAndClaimantAsync(
        Guid eventId,
        Guid claimantActorId,
        bool trackChanges,
        CancellationToken cancellationToken)
    {
        return DetailsQuery(trackChanges)
            .OrderByDescending(claim => claim.CreatedAt)
            .FirstOrDefaultAsync(
                claim => claim.EventId == eventId && claim.ClaimantActorId == claimantActorId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<EventOrganizerClaim>> ListByEventAsync(
        Guid eventId,
        CancellationToken cancellationToken)
    {
        return await DetailsQuery(trackChanges: false)
            .Where(claim => claim.EventId == eventId)
            .OrderByDescending(claim => claim.CreatedAt)
            .ThenBy(claim => claim.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EventOrganizerClaim>> ListByClaimantAsync(
        Guid claimantActorId,
        CancellationToken cancellationToken)
    {
        return await DetailsQuery(trackChanges: false)
            .Where(claim => claim.ClaimantActorId == claimantActorId)
            .OrderByDescending(claim => claim.CreatedAt)
            .ThenBy(claim => claim.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task UpdateApprovalAsync(
        EventOrganizerClaim claim,
        Event @event,
        CancellationToken cancellationToken)
    {
        dbContext.Entry(claim).State = EntityState.Modified;
        dbContext.Entry(@event).State = EntityState.Modified;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<EventOrganizerClaim> DetailsQuery(bool trackChanges)
    {
        IQueryable<EventOrganizerClaim> query = dbContext.EventOrganizerClaims
            .Include(claim => claim.Status)
            .Include(claim => claim.ClaimantActor)
                .ThenInclude(actor => actor!.Pii)
            .Include(claim => claim.Event)
                .ThenInclude(@event => @event!.Actor)
            .Include(claim => claim.Event)
                .ThenInclude(@event => @event!.EventProvenanceType);

        return trackChanges ? query : query.AsNoTracking();
    }
}
