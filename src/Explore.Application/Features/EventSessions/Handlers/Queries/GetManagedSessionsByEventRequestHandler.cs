using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSession;
using Explore.Application.Features.EventSessions.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventSessions.Handlers.Queries;

public class GetManagedSessionsByEventRequestHandler : IRequestHandler<GetManagedSessionsByEventRequest, List<EventSessionListDto>>
{
    private readonly IEventSessionRepository _eventSessionRepository;

    public GetManagedSessionsByEventRequestHandler(
        IEventSessionRepository eventSessionRepository)
    {
        _eventSessionRepository = eventSessionRepository;
    }

    public async Task<List<EventSessionListDto>> Handle(
        GetManagedSessionsByEventRequest request,
        CancellationToken cancellationToken)
    {
        var eventSessions = await _eventSessionRepository.GetSessionsByEvent(request.EventId);
        var dtos = eventSessions.Select(EventSessionMapper.ToListItem).ToList();
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
