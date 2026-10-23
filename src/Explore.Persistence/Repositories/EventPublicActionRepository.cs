using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class EventPublicActionRepository(ExploreDbContext dbContext, TimeProvider? timeProvider = null)
    : GenericRepository<EventPublicAction, Guid>(dbContext), IEventPublicActionRepository
{
    public Task<Event?> GetAuthorizedEventForMutationAsync(
        Guid tenantId, Guid userId, Guid eventId, CancellationToken cancellationToken) =>
        new EventMutationAuthority(dbContext, timeProvider)
            .ForPublicActionsAsync(tenantId, userId, eventId, cancellationToken);

    public Task<EventPublicAction?> GetDetailsAsync(Guid id, bool trackChanges, CancellationToken cancellationToken)
    {
        return DetailsQuery(trackChanges)
            .FirstOrDefaultAsync(action => action.Id == id, cancellationToken);
    }

    public async Task<EventPublicAction?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        var persisted = await DetailsQuery(trackChanges: false)
            .FirstOrDefaultAsync(action => action.Id == id, cancellationToken);
        if (persisted is null)
            return null;
        var tracked = dbContext.EventPublicActions.Local.FirstOrDefault(action => action.Id == id);
        if (tracked is not null)
        {
            // A previously tracked object cannot rewrite the persisted parent used
            // by the caller's exact-event authority check.
            if (tracked.TenantId != persisted.TenantId || tracked.EventId != persisted.EventId
                || tracked.ConcurrencyStamp != persisted.ConcurrencyStamp || tracked.IsDeleted)
                return null;
            return tracked;
        }
        dbContext.Entry(persisted).State = EntityState.Unchanged;
        return persisted;
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
