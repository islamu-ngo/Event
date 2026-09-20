using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventDay;
using Explore.Application.Features.EventDays.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventDays.Handlers.Queries;

public class GetEventDayDetailRequestHandler : IQueryHandler<GetEventDayDetailRequest, EventDayDto?>
{
    private readonly IEventRepository _eventRepository;
    private readonly IEventDayRepository _eventDayRepository;

    public GetEventDayDetailRequestHandler(
        IEventRepository eventRepository,
        IEventDayRepository eventDayRepository)
    {
        _eventRepository = eventRepository;
        _eventDayRepository = eventDayRepository;
    }

    public async Task<EventDayDto?> QueryAsync(GetEventDayDetailRequest request, CancellationToken cancellationToken)
    {
        var eventDay = await _eventDayRepository.GetById(request.Id);
        if (eventDay == null)
            return null;

        if (!await _eventRepository.IsPubliclyEligibleAsync(
                eventDay.TenantId,
                eventDay.EventId,
                cancellationToken))
            return null;

        return EventMapper.ToDetail(eventDay);
    }
}
