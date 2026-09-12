using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSeries;
using Explore.Application.Features.EventSeries.Requests.Queries;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSeries.Handlers.Queries;

public class GetEventSeriesListRequestHandler : IRequestHandler<GetEventSeriesListRequest, PaginatedResult<EventSeriesListDto>>
{
    private readonly IEventSeriesRepository _eventSeriesRepository;

    public GetEventSeriesListRequestHandler(IEventSeriesRepository eventSeriesRepository)
    {
        _eventSeriesRepository = eventSeriesRepository;
    }

    public async Task<PaginatedResult<EventSeriesListDto>> Handle(GetEventSeriesListRequest request, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _eventSeriesRepository.GetEventSeriesPaged(request.PageNumber, request.PageSize, request.ActorId);
        var dtos = items.Select(EventMapper.ToListItem).ToList();

        return PaginatedResult<EventSeriesListDto>.Create(
            dtos,
            totalCount,
            request.PageNumber,
            request.PageSize);
    }
}
