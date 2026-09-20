using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSeries;
using Explore.Application.Features.EventSeries.Requests.Queries;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventSeries.Handlers.Queries;

public class GetTopEventSeriesRequestHandler : IQueryHandler<GetTopEventSeriesRequest, EventSeriesDto?>
{
    private readonly IEventSeriesRepository _eventSeriesRepository;
    private readonly TimeProvider _timeProvider;

    public GetTopEventSeriesRequestHandler(IEventSeriesRepository eventSeriesRepository, TimeProvider timeProvider)
    {
        _eventSeriesRepository = eventSeriesRepository;
        _timeProvider = timeProvider;
    }

    public async Task<EventSeriesDto?> QueryAsync(GetTopEventSeriesRequest request, CancellationToken cancellationToken)
    {
        var series = await _eventSeriesRepository.GetTopEventSeries(_timeProvider.GetUtcNow(), cancellationToken);
        if (series == null)
        {
            return null;
        }

        return EventMapper.ToDetail(series);
    }
}
