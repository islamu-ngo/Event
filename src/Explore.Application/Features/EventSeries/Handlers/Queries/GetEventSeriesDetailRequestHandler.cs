using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSeries;
using Explore.Application.Features.EventSeries.Requests.Queries;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventSeries.Handlers.Queries;

public class GetEventSeriesDetailRequestHandler : IQueryHandler<GetEventSeriesDetailRequest, EventSeriesDto?>
{
    private readonly IEventSeriesRepository _eventSeriesRepository;

    public GetEventSeriesDetailRequestHandler(IEventSeriesRepository eventSeriesRepository)
    {
        _eventSeriesRepository = eventSeriesRepository;
    }

    public async Task<EventSeriesDto?> QueryAsync(GetEventSeriesDetailRequest request, CancellationToken cancellationToken)
    {
        var series = await _eventSeriesRepository.GetEventSeriesWithEvents(request.Id, cancellationToken);
        if (series == null)
        {
            return null;
        }

        return EventMapper.ToDetail(series);
    }
}
