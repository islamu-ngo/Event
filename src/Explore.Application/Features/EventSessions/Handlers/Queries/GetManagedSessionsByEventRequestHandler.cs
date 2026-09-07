using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSession;
using Explore.Application.Features.EventSessions.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventSessions.Handlers.Queries;

public class GetManagedSessionsByEventRequestHandler : IRequestHandler<GetManagedSessionsByEventRequest, List<EventSessionListDto>>
{
    private readonly IEventSessionRepository _eventSessionRepository;
    private readonly IMapper _mapper;

    public GetManagedSessionsByEventRequestHandler(
        IEventSessionRepository eventSessionRepository,
        IMapper mapper)
    {
        _eventSessionRepository = eventSessionRepository;
        _mapper = mapper;
    }

    public async Task<List<EventSessionListDto>> Handle(
        GetManagedSessionsByEventRequest request,
        CancellationToken cancellationToken)
    {
        var eventSessions = await _eventSessionRepository.GetSessionsByEvent(request.EventId);
        var dtos = _mapper.Map<List<EventSessionListDto>>(eventSessions);
        for (var index = 0; index < dtos.Count; index++)
        {
            var session = eventSessions[index];
            var dto = dtos[index];
            dto.LocationId = session.LocationId;
            dto.LocationFullName = session.Location?.FullName;
            dto.LocationCity = session.Location?.City;
            dto.RoomId = session.RoomId;
            dto.RoomName = session.Room?.Name;
        }

        return dtos;
    }
}
