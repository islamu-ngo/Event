using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSeries;
using Explore.Application.Features.EventSeries.Requests.Queries;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSeries.Handlers.Queries;

public class GetEventSeriesDetailRequestHandler : IRequestHandler<GetEventSeriesDetailRequest, EventSeriesDto?>
{
    private readonly IEventSeriesRepository _eventSeriesRepository;
    private readonly IMapper _mapper;

    public GetEventSeriesDetailRequestHandler(IEventSeriesRepository eventSeriesRepository, IMapper mapper)
    {
        _eventSeriesRepository = eventSeriesRepository;
        _mapper = mapper;
    }

    public async Task<EventSeriesDto?> Handle(GetEventSeriesDetailRequest request, CancellationToken cancellationToken)
    {
        var series = await _eventSeriesRepository.GetEventSeriesWithEvents(request.Id);
        if (series == null)
        {
            return null;
        }

        return _mapper.Map<EventSeriesDto>(series);
    }
}
