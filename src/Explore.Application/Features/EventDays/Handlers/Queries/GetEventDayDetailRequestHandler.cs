using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventDay;
using Explore.Application.Features.EventDays.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventDays.Handlers.Queries;

public class GetEventDayDetailRequestHandler : IRequestHandler<GetEventDayDetailRequest, EventDayDto?>
{
    private readonly IEventRepository _eventRepository;
    private readonly IEventDayRepository _eventDayRepository;
    private readonly IMapper _mapper;

    public GetEventDayDetailRequestHandler(
        IEventRepository eventRepository,
        IEventDayRepository eventDayRepository,
        IMapper mapper)
    {
        _eventRepository = eventRepository;
        _eventDayRepository = eventDayRepository;
        _mapper = mapper;
    }

    public async Task<EventDayDto?> Handle(GetEventDayDetailRequest request, CancellationToken cancellationToken)
    {
        var eventDay = await _eventDayRepository.GetById(request.Id);
        if (eventDay == null)
            return null;

        if (!await _eventRepository.IsPubliclyEligibleAsync(
                eventDay.TenantId,
                eventDay.EventId,
                cancellationToken))
            return null;

        return _mapper.Map<EventDayDto>(eventDay);
    }
}
