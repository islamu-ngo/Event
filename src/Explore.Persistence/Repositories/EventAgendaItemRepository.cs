using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class EventAgendaItemRepository : GenericRepository<EventAgendaItem, Guid>, IEventAgendaItemRepository
{
    private readonly ExploreDbContext _dbContext;

    public EventAgendaItemRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<EventAgendaItem>> GetByEventAsync(Guid eventId, CancellationToken cancellationToken)
    {
        return await _dbContext.EventAgendaItems
            .AsNoTracking()
            .Where(a => a.EventId == eventId)
            .OrderBy(a => a.SortOrder)
            .ThenBy(a => a.StartTime)
            .ToListAsync(cancellationToken);
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
        return await _dbContext.EventAgendaItems
            .AsNoTracking()
            .WherePubliclyEligible(_dbContext)
            .Where(item => item.EventId == eventId)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.StartTime)
            .ToListAsync(cancellationToken);
    }

    public async Task MoveToEventAsync(
        EventAgendaItem agendaItem,
        Guid eventId,
        EventLocation eventLocation,
        Guid? roomId,
        CancellationToken cancellationToken)
    {
        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Moving an event agenda item requires an active transaction.");
        }

        _dbContext.Entry(agendaItem).State = EntityState.Detached;
        int affectedRows = await _dbContext.EventAgendaItems
            .Where(candidate =>
                candidate.TenantId == agendaItem.TenantId &&
                candidate.Id == agendaItem.Id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(candidate => candidate.EventId, eventId)
                    .SetProperty(candidate => candidate.EventLocationId, eventLocation.Id)
                    .SetProperty(candidate => candidate.LocationId, eventLocation.LocationId)
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
