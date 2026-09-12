using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSeries;
using Explore.Application.Features.EventSeries.Requests.Queries;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSeries.Handlers.Queries;

public class GetEventSeriesDetailRequestHandler : IRequestHandler<GetEventSeriesDetailRequest, EventSeriesDto?>
{
    private readonly IEventSeriesRepository _eventSeriesRepository;

    public GetEventSeriesDetailRequestHandler(IEventSeriesRepository eventSeriesRepository)
    {
        _eventSeriesRepository = eventSeriesRepository;
    }

    public async Task<EventSeriesDto?> Handle(GetEventSeriesDetailRequest request, CancellationToken cancellationToken)
    {
        var series = await _eventSeriesRepository.GetEventSeriesWithEvents(request.Id);
        if (series == null)
        {
            return null;
        }

        return EventMapper.ToDetail(series);
    }
}
