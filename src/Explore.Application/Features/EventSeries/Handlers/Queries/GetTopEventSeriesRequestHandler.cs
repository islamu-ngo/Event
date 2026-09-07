using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSeries;
using Explore.Application.Features.EventSeries.Requests.Queries;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSeries.Handlers.Queries;

public class GetTopEventSeriesRequestHandler : IRequestHandler<GetTopEventSeriesRequest, EventSeriesDto?>
{
    private readonly IEventSeriesRepository _eventSeriesRepository;
    private readonly IMapper _mapper;

    public GetTopEventSeriesRequestHandler(IEventSeriesRepository eventSeriesRepository, IMapper mapper)
    {
        _eventSeriesRepository = eventSeriesRepository;
        _mapper = mapper;
    }

    public async Task<EventSeriesDto?> Handle(GetTopEventSeriesRequest request, CancellationToken cancellationToken)
    {
        var series = await _eventSeriesRepository.GetTopEventSeries(DateTimeOffset.UtcNow);
        if (series == null)
        {
            return null;
        }

        return _mapper.Map<EventSeriesDto>(series);
    }
}
