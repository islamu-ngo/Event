using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Extensions;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public class EventAgendaItemRepository : GenericRepository<EventAgendaItem, Guid>, IEventAgendaItemRepository
{
    private readonly ExploreDbContext _dbContext;
    private readonly IHttpContextAccessor _httpContext;

    public EventAgendaItemRepository(ExploreDbContext dbContext, IHttpContextAccessor httpContext) : base(dbContext)
    {
        _dbContext = dbContext;
        _httpContext = httpContext;
    }

    public Task<Event?> GetAuthorizedParentForMutationAsync(
        Guid tenantId, Guid userId, Guid eventId, string action, CancellationToken cancellationToken) =>
        new EventMutationAuthority(_dbContext, null).ForAgendaItemAsync(
            tenantId, userId, eventId, action, _httpContext, cancellationToken);

    public async Task<EventAgendaItem?> GetForMutationAsync(Guid id, CancellationToken cancellationToken)
    {
        if (_dbContext.TenantFilterTenantId is not { } tenantId || tenantId == Guid.Empty
            || _dbContext.IsTenantFilterBypassed
            || _dbContext.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Agenda mutation requires the exact tenant Serializable transaction.");
        await RelationalEntityRowFence.AcquireAsync<EventAgendaItem>(
            _dbContext, tenantId, item => item.Id, id, cancellationToken);
        var current = await _dbContext.EventAgendaItems.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id && item.TenantId == tenantId, cancellationToken);
        var tracked = _dbContext.ChangeTracker.Entries<EventAgendaItem>().SingleOrDefault(entry => entry.Entity.Id == id);
        if (tracked is not null && current is not null
            && (tracked.Entity.TenantId != current.TenantId || tracked.Entity.EventId != current.EventId
                || tracked.Entity.ConcurrencyStamp != current.ConcurrencyStamp))
            throw new InvalidOperationException("Tracked agenda ownership must match the persisted row.");
        if (tracked is not null)
            tracked.State = EntityState.Detached;
        return current;
    }

    public async Task<List<EventAgendaItem>> GetByEventAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var items = await _dbContext.EventAgendaItems
            .AsNoTracking()
            .Where(a => a.EventId == eventId)
            .ToListAsync(cancellationToken);

        return items.OrderBy(item => item.SortOrder).ThenBy(item => item.StartTime).ToList();
    }

    public async Task<EventAgendaItem?> GetPublicByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.EventAgendaItems
            .AsNoTracking()
            .WherePubliclyEligible(_dbContext)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    public async Task<List<EventAgendaItem>> GetPublicByEventAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var items = await _dbContext.EventAgendaItems
            .AsNoTracking()
            .WherePubliclyEligible(_dbContext)
            .Where(item => item.EventId == eventId)
            .ToListAsync(cancellationToken);

        return items.OrderBy(item => item.SortOrder).ThenBy(item => item.StartTime).ToList();
    }

    public async Task MoveToEventAsync(
        EventAgendaItem agendaItem,
        Guid eventId,
        EventLocation eventLocation,
        Guid? roomId,
        CancellationToken cancellationToken)
    {
        var current = await GetForMutationAsync(agendaItem.Id, cancellationToken);
        if (current is null || current.TenantId != agendaItem.TenantId
            || current.EventId != agendaItem.EventId || current.ConcurrencyStamp != agendaItem.ConcurrencyStamp)
            throw new InvalidOperationException("The agenda move must use its current persisted source.");
        foreach (Guid parentId in new[] { current.EventId, eventId }.Distinct().Order())
        {
            await RelationalEntityRowFence.AcquireAsync<Event>(
                _dbContext, current.TenantId, parent => parent.Id, parentId, cancellationToken);
            if (!await _dbContext.Events.AsNoTracking().AnyAsync(
                    parent => parent.Id == parentId && parent.TenantId == current.TenantId, cancellationToken))
                throw new InvalidOperationException("The agenda move requires active same-tenant parents.");
        }
        await RelationalEntityRowFence.AcquireAsync<EventLocation>(
            _dbContext, current.TenantId, placement => placement.Id, eventLocation.Id, cancellationToken);
        var placement = await _dbContext.EventLocations.AsNoTracking().SingleOrDefaultAsync(
            row => row.Id == eventLocation.Id && row.TenantId == current.TenantId && row.EventId == eventId,
            cancellationToken);
        if (placement is null || eventLocation.TenantId != placement.TenantId
            || eventLocation.EventId != placement.EventId || eventLocation.LocationId != placement.LocationId)
            throw new InvalidOperationException("The agenda move requires its current persisted destination placement.");
        if (roomId is { } selectedRoom)
        {
            await RelationalEntityRowFence.AcquireAsync<LocationRoom>(
                _dbContext, current.TenantId, room => room.Id, selectedRoom, cancellationToken);
            if (!await _dbContext.LocationRooms.AsNoTracking().AnyAsync(
                    room => room.Id == selectedRoom && room.TenantId == current.TenantId
                        && room.LocationId == placement.LocationId, cancellationToken))
                throw new InvalidOperationException("The agenda room must belong to its destination location.");
        }

        _dbContext.DisclosureMutations.Enlist([current.TenantId]);
        _dbContext.Entry(agendaItem).State = EntityState.Detached;
        int affectedRows = await _dbContext.EventAgendaItems
            .Where(candidate =>
                candidate.TenantId == current.TenantId &&
                candidate.Id == current.Id && candidate.EventId == current.EventId
                && candidate.ConcurrencyStamp == current.ConcurrencyStamp)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(candidate => candidate.EventId, eventId)
                    .SetProperty(candidate => candidate.EventLocationId, placement.Id)
                    .SetProperty(candidate => candidate.LocationId, placement.LocationId)
                    .SetProperty(candidate => candidate.RoomId, roomId)
                    .SetProperty(candidate => candidate.EventDayId, (Guid?)null),
            cancellationToken);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException("The event agenda item could not be moved because it is no longer active.");
        }

        agendaItem.EventId = eventId;
        agendaItem.EventDayId = null;
        agendaItem.AssignEventLocation(eventLocation);
        agendaItem.RoomId = roomId;
        _dbContext.EventAgendaItems.Attach(agendaItem);
    }
}
