using Explore.Application.DTOs.EventSeries;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventSeries.Requests.Queries;

public sealed record GetEventSeriesListRequest : IQuery<PaginatedResult<EventSeriesListDto>>
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 10;
    public Guid? ActorId { get; init; }
}
