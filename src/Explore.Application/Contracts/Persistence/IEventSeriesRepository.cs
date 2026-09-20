using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEventSeriesRepository : IGenericRepository<EventSeries, Guid>
{
    Task<EventSeries?> GetEventSeriesBySlug(string slug, CancellationToken cancellationToken = default);
    Task<(List<EventSeries> Items, int TotalCount)> GetEventSeriesPaged(int pageNumber, int pageSize, Guid? actorId = null, CancellationToken cancellationToken = default);
    Task<EventSeries?> GetEventSeriesWithEvents(Guid id, CancellationToken cancellationToken = default);
    Task<EventSeries?> GetTopEventSeries(DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<EventSeries?> GetForUpdateAsync(Guid id, Guid tenantId, CancellationToken cancellationToken);
}
