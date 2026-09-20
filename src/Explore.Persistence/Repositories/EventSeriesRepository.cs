using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Specifications.Events;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class EventSeriesRepository : GenericRepository<EventSeries, Guid>, IEventSeriesRepository
{
    private readonly ExploreDbContext _dbContext;

    public EventSeriesRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public override async Task Update(EventSeries entity)
    {
        try
        {
            await base.Update(entity);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException(
                ConcurrencyConflictException.ConcurrentUpdate,
                "The event series was modified by another request. Reload and retry.",
                nameof(EventSeries), entity.Id.ToString(), exception);
        }
    }

    public Task<EventSeries?> GetEventSeriesBySlug(string slug, CancellationToken cancellationToken = default) =>
        PublicSeries()
            .Include(series => series.Actor).ThenInclude(actor => actor.Pii)
            .Include(series => series.FeaturedImage)
            .FirstOrDefaultAsync(series => series.Slug == slug, cancellationToken);

    public async Task<(List<EventSeries> Items, int TotalCount)> GetEventSeriesPaged(
        int pageNumber, int pageSize, Guid? actorId = null, CancellationToken cancellationToken = default)
    {
        var eligibleEventIds = _dbContext.Events.WherePubliclyEligible(_dbContext).Select(item => item.Id);
        var query = PublicSeries()
            .Include(series => series.Actor).ThenInclude(actor => actor.Pii)
            .Include(series => series.FeaturedImage)
            .Include(series => series.Events.Where(item => eligibleEventIds.Contains(item.Id)))
            .AsQueryable();

        if (actorId.HasValue)
            query = query.Where(series => series.ActorId == actorId.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(series => series.CreatedAt).ThenBy(series => series.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (items, totalCount);
    }

    public Task<EventSeries?> GetEventSeriesWithEvents(Guid id, CancellationToken cancellationToken = default) =>
        PublicGraph(_dbContext.Events.WherePubliclyEligible(_dbContext).Select(item => item.Id))
            .FirstOrDefaultAsync(series => series.Id == id, cancellationToken);

    public Task<EventSeries?> GetTopEventSeries(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var eligibleEvents = _dbContext.Events.WherePubliclyEligible(_dbContext);
        // Preserve undated events as candidates. The shared temporal primitive owns
        // exact-instant SQL translation; ranking and paging remain in the database.
        var upcomingIds = EventDirectoryTemporalQuery.Apply(_dbContext, eligibleEvents,
                TemporalView.UpcomingAndOngoing, now).Select(item => item.Id)
            .Concat(eligibleEvents.Where(item => item.LastSessionEndUtc == null).Select(item => item.Id));
        return PublicGraph(upcomingIds)
            .Where(series => series.Events.Any(item => upcomingIds.Contains(item.Id)))
            .OrderByDescending(series => series.Events.Count(item => upcomingIds.Contains(item.Id)))
            .ThenByDescending(series => series.TotalViews).ThenBy(series => series.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    // Mutation authority must not depend on public publication rules or EF's identity map.
    public Task<EventSeries?> GetForUpdateAsync(Guid id, Guid tenantId, CancellationToken cancellationToken) =>
        _dbContext.EventSeries.AsNoTracking().Include(series => series.Events)
            .FirstOrDefaultAsync(series => series.Id == id && series.TenantId == tenantId && !series.IsDeleted,
                cancellationToken);

    private IQueryable<EventSeries> PublicSeries() => _dbContext.EventSeries.AsNoTrackingWithIdentityResolution()
        .Where(series => series.IsPublished && series.VisibilityTypeId == (int)VisibilityTypeEnum.Public);

    private IQueryable<EventSeries> PublicGraph(IQueryable<Guid> eligibleEventIds) => PublicSeries()
        .AsSplitQuery()
        .Include(series => series.Actor).ThenInclude(actor => actor.Pii)
        .Include(series => series.FeaturedImage)
        .Include(series => series.Events.Where(item => eligibleEventIds.Contains(item.Id)))
            .ThenInclude(item => item.EventType)
        .Include(series => series.Events.Where(item => eligibleEventIds.Contains(item.Id)))
            .ThenInclude(item => item.FeaturedImage)
        .Include(series => series.Events.Where(item => eligibleEventIds.Contains(item.Id)))
            .ThenInclude(item => item.ParticipationConfiguration)
        .Include(series => series.Events.Where(item => eligibleEventIds.Contains(item.Id)))
            .ThenInclude(item => item.TicketCatalogVersions.Where(catalog =>
                !catalog.IsDeleted && catalog.TicketCatalogStatusId == (int)TicketCatalogStatusEnum.Published))
                .ThenInclude(catalog => catalog.TicketTypes.Where(ticketType => !ticketType.IsDeleted));
}
