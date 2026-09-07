using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class EventSeriesRepository : GenericRepository<EventSeries, Guid>, IEventSeriesRepository
{
    private readonly ExploreDbContext _dbContext;

    public EventSeriesRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<EventSeries?> GetEventSeriesBySlug(string slug)
    {
        return await _dbContext.EventSeries
            .Include(e => e.Actor)
                .ThenInclude(a => a.Pii)
            .Include(e => e.FeaturedImage)
            .FirstOrDefaultAsync(q => q.Slug == slug);
    }

    public async Task<(List<EventSeries> Items, int TotalCount)> GetEventSeriesPaged(int pageNumber, int pageSize, Guid? actorId = null)
    {
        var query = _dbContext.EventSeries
            .AsNoTracking()
            .Include(e => e.Actor)
                .ThenInclude(a => a.Pii)
            .Include(e => e.FeaturedImage)
            .Include(e => e.Events)
            .AsQueryable();

        if (actorId.HasValue)
        {
            query = query.Where(q => q.ActorId == actorId.Value);
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(q => q.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<EventSeries?> GetEventSeriesWithEvents(Guid id)
    {
        return await _dbContext.EventSeries
            .AsSplitQuery()
            .Include(e => e.Actor)
                .ThenInclude(a => a.Pii)
            .Include(e => e.FeaturedImage)
            .Include(e => e.Events)
                .ThenInclude(ev => ev.EventType)
            .Include(e => e.Events)
                .ThenInclude(ev => ev.FeaturedImage)
            .Include(e => e.Events)
                .ThenInclude(ev => ev.ParticipationConfiguration)
            .Include(e => e.Events)
                .ThenInclude(ev => ev.TicketCatalogVersions.Where(catalog =>
                    !catalog.IsDeleted && catalog.TicketCatalogStatusId == (int)TicketCatalogStatusEnum.Published))
                    .ThenInclude(catalog => catalog.TicketTypes.Where(ticketType => !ticketType.IsDeleted))
            .FirstOrDefaultAsync(q => q.Id == id);
    }

    public async Task<EventSeries?> GetTopEventSeries(DateTimeOffset now)
    {
        // Logic: published series that have at least one upcoming/ongoing event
        // Ordered by: number of upcoming events, then total views
        return await _dbContext.EventSeries
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Include(e => e.FeaturedImage)
            .Include(e => e.Actor)
                .ThenInclude(a => a.Pii)
            .Include(e => e.Events.Where(ev => ev.LastSessionEndUtc == null || ev.LastSessionEndUtc > now))
                .ThenInclude(ev => ev.EventType)
            .Include(e => e.Events.Where(ev => ev.LastSessionEndUtc == null || ev.LastSessionEndUtc > now))
                .ThenInclude(ev => ev.FeaturedImage)
            .Include(e => e.Events.Where(ev => ev.LastSessionEndUtc == null || ev.LastSessionEndUtc > now))
                .ThenInclude(ev => ev.ParticipationConfiguration)
            .Include(e => e.Events.Where(ev => ev.LastSessionEndUtc == null || ev.LastSessionEndUtc > now))
                .ThenInclude(ev => ev.TicketCatalogVersions.Where(catalog =>
                    !catalog.IsDeleted && catalog.TicketCatalogStatusId == (int)TicketCatalogStatusEnum.Published))
                    .ThenInclude(catalog => catalog.TicketTypes.Where(ticketType => !ticketType.IsDeleted))
            .Where(q => q.IsPublished && !q.IsDeleted)
            .Where(q => q.Events.Any(ev => ev.LastSessionEndUtc == null || ev.LastSessionEndUtc > now))
            .OrderByDescending(q => q.Events.Count(ev => ev.LastSessionEndUtc == null || ev.LastSessionEndUtc > now))
            .ThenByDescending(q => q.TotalViews)
            .FirstOrDefaultAsync();
    }
}
