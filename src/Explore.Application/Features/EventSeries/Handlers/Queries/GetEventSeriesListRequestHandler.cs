using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSeries;
using Explore.Application.Features.EventSeries.Requests.Queries;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSeries.Handlers.Queries;

public class GetEventSeriesListRequestHandler : IRequestHandler<GetEventSeriesListRequest, PaginatedResult<EventSeriesListDto>>
{
    private readonly IEventSeriesRepository _eventSeriesRepository;
    private readonly IMapper _mapper;

    public GetEventSeriesListRequestHandler(IEventSeriesRepository eventSeriesRepository, IMapper mapper)
    {
        _eventSeriesRepository = eventSeriesRepository;
        _mapper = mapper;
    }

    public async Task<PaginatedResult<EventSeriesListDto>> Handle(GetEventSeriesListRequest request, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _eventSeriesRepository.GetEventSeriesPaged(request.PageNumber, request.PageSize, request.ActorId);
        var dtos = _mapper.Map<List<EventSeriesListDto>>(items);

        return PaginatedResult<EventSeriesListDto>.Create(
            dtos,
            totalCount,
            request.PageNumber,
            request.PageSize);
    }
}
