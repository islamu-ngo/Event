using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEventSeriesRepository : IGenericRepository<EventSeries, Guid>
{
    Task<EventSeries?> GetEventSeriesBySlug(string slug);
    Task<(List<EventSeries> Items, int TotalCount)> GetEventSeriesPaged(int pageNumber, int pageSize, Guid? actorId = null);
    Task<EventSeries?> GetEventSeriesWithEvents(Guid id);
    Task<EventSeries?> GetTopEventSeries(DateTimeOffset now);
}
