using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Models;

namespace Explore.Blazor.Client.Contracts.Services.Events;

public interface IEventSeriesService
{
    Task<PaginatedResult<EventSeriesListDto>> GetSeriesListAsync(
        int pageNumber = 1, int pageSize = 10, Guid? actorId = null, CancellationToken ct = default);

    Task<EventSeriesDto?> GetSeriesDetailAsync(Guid id, CancellationToken ct = default);

    Task<BaseCommandResponseOfGuid?> CreateSeriesAsync(CreateEventSeriesDto dto, CancellationToken ct = default);

    Task<BaseCommandResponseOfGuid?> UpdateSeriesAsync(
        Guid id,
        Guid expectedConcurrencyStamp,
        UpdateEventSeriesDto dto,
        CancellationToken ct = default);

    /// <summary>
    /// Searches series by title substring for autocomplete.
    /// Returns at most <paramref name="maxResults"/> items.
    /// </summary>
    Task<IEnumerable<EventSeriesListDto>> SearchSeriesAsync(string query, int maxResults = 10, CancellationToken ct = default);
}
