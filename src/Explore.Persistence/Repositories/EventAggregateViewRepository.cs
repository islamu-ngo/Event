using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Views;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class EventAggregateViewRepository : IEventAggregateViewRepository
{
    private readonly ExploreDbContext _dbContext;

    public EventAggregateViewRepository(ExploreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<EventWithSessionsView?> GetByEventIdAsync(Guid eventId, CancellationToken cancellationToken)
    {
        return await _dbContext.EventsWithSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.EventId == eventId, cancellationToken);
    }

    public async Task<(List<EventWithSessionsView> Items, int TotalCount)> GetPagedAsync(
        EventAggregateViewFilter filter,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = ApplyFilters(_dbContext.EventsWithSessions.AsNoTracking(), filter);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.StartAt)
            .ThenBy(x => x.Title)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<List<EventCustomPropertyDefinition>> GetEventDefinitionsByEventIdsAsync(
        IReadOnlyCollection<Guid> eventIds,
        CancellationToken cancellationToken)
    {
        if (eventIds.Count == 0)
            return [];

        return await _dbContext.EventCustomPropertyDefinitions
            .AsNoTracking()
            .Where(x => eventIds.Contains(x.EventId))
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<EventSessionCustomPropertyDefinition>> GetSessionDefinitionsForEventAsync(
        Guid eventId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.EventSessionCustomPropertyDefinitions
            .AsNoTracking()
            .Where(x => x.EventSession != null && x.EventSession.EventId == eventId)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<EventWithSessionsView> ApplyFilters(
        IQueryable<EventWithSessionsView> query,
        EventAggregateViewFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Title))
        {
            var title = $"%{filter.Title.Trim().ToLowerInvariant()}%";
            query = query.Where(x => EF.Functions.Like(x.Title.ToLower(), title));
        }

        if (filter.StartAtFrom.HasValue)
            query = query.Where(x => x.StartAt >= filter.StartAtFrom.Value);

        if (filter.StartAtTo.HasValue)
            query = query.Where(x => x.StartAt <= filter.StartAtTo.Value);

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var status = filter.Status.Trim().ToLowerInvariant();
            query = query.Where(x => EF.Functions.Like(x.Status.ToLower(), status));
        }

        if (!string.IsNullOrWhiteSpace(filter.Visibility))
        {
            var visibility = filter.Visibility.Trim().ToLowerInvariant();
            query = query.Where(x => EF.Functions.Like(x.Visibility.ToLower(), visibility));
        }

        return query;
    }
}
