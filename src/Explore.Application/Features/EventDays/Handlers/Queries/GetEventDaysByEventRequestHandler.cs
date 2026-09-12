using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventDay;
using Explore.Application.Features.EventDays.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventDays.Handlers.Queries;

public class GetEventDaysByEventRequestHandler :
    IRequestHandler<GetEventDaysByEventRequest, List<EventDayListDto>>
{
    private readonly IEventRepository _eventRepository;
    private readonly IEventDayRepository _eventDayRepository;

    public GetEventDaysByEventRequestHandler(
        IEventRepository eventRepository,
        IEventDayRepository eventDayRepository)
    {
        _eventRepository = eventRepository;
        _eventDayRepository = eventDayRepository;
    }

    public async Task<List<EventDayListDto>> Handle(GetEventDaysByEventRequest request, CancellationToken cancellationToken)
    {
        var parentEvent = await _eventRepository.GetById(request.EventId);
        if (parentEvent is null || !await _eventRepository.IsPubliclyEligibleAsync(
                parentEvent.TenantId,
                parentEvent.Id,
                cancellationToken))
            return [];

        var eventDays = await _eventDayRepository.GetByEventAsync(request.EventId, cancellationToken);
        return eventDays.Select(EventMapper.ToListItem).ToList();
    }

}

public sealed class GetManagedEventDaysByEventRequestHandler(
    IEventDayRepository eventDayRepository)
    : IRequestHandler<GetManagedEventDaysByEventRequest, List<EventDayListDto>>
{
    public async Task<List<EventDayListDto>> Handle(
        GetManagedEventDaysByEventRequest request,
        CancellationToken cancellationToken)
    {
        var eventDays = await eventDayRepository.GetByEventAsync(request.EventId, cancellationToken);
        return eventDays.Select(EventMapper.ToListItem).ToList();
    }
}
