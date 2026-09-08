using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class EventPublicActionRepository(ExploreDbContext dbContext)
    : GenericRepository<EventPublicAction, Guid>(dbContext), IEventPublicActionRepository
{
    public Task<EventPublicAction?> GetDetailsAsync(Guid id, bool trackChanges, CancellationToken cancellationToken)
    {
        return DetailsQuery(trackChanges)
            .FirstOrDefaultAsync(action => action.Id == id, cancellationToken);
    }

    public Task<EventPublicAction?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        return DetailsQuery(trackChanges: true)
            .FirstOrDefaultAsync(action => action.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<EventPublicAction>> ListByEventAsync(
        Guid eventId,
        bool trackChanges,
        CancellationToken cancellationToken)
    {
        return await DetailsQuery(trackChanges)
            .Where(action => action.EventId == eventId)
            .OrderBy(action => action.SortOrder)
            .ThenBy(action => action.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> HasOtherPrimaryAsync(
        Guid eventId,
        Guid? excludedActionId,
        CancellationToken cancellationToken)
    {
        return dbContext.EventPublicActions
            .AsNoTracking()
            .AnyAsync(
                action => action.EventId == eventId
                    && action.IsPrimary
                    && (!excludedActionId.HasValue || action.Id != excludedActionId.Value),
                cancellationToken);
    }

    private IQueryable<EventPublicAction> DetailsQuery(bool trackChanges)
    {
        IQueryable<EventPublicAction> query = dbContext.EventPublicActions
            .Include(action => action.EventPublicActionKind)
            .Include(action => action.HealthState)
            .Include(action => action.Event)
                .ThenInclude(@event => @event!.Actor)
            .Include(action => action.Event)
                .ThenInclude(@event => @event!.EventProvenanceType);

        return trackChanges ? query : query.AsNoTracking();
    }
}
