using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSeries;
using Explore.Application.Features.EventSeries.Requests.Queries;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSeries.Handlers.Queries;

public class GetTopEventSeriesRequestHandler : IRequestHandler<GetTopEventSeriesRequest, EventSeriesDto?>
{
    private readonly IEventSeriesRepository _eventSeriesRepository;

    public GetTopEventSeriesRequestHandler(IEventSeriesRepository eventSeriesRepository)
    {
        _eventSeriesRepository = eventSeriesRepository;
    }

    public async Task<EventSeriesDto?> Handle(GetTopEventSeriesRequest request, CancellationToken cancellationToken)
    {
        var series = await _eventSeriesRepository.GetTopEventSeries(DateTimeOffset.UtcNow);
        if (series == null)
        {
            return null;
        }

        return EventMapper.ToDetail(series);
    }
}
