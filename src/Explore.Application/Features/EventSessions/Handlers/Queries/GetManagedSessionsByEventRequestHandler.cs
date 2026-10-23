using Explore.Application.Mappings;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSession;
using Explore.Application.Features.EventSessions.Requests.Queries;

namespace Explore.Application.Features.EventSessions.Handlers.Queries;

public class GetManagedSessionsByEventRequestHandler : IQueryHandler<GetManagedSessionsByEventRequest, List<EventSessionListDto>>
{
    private readonly IEventSessionRepository _eventSessionRepository;
    private readonly IPrivateEventManagementReader? _privateManagement;

    public GetManagedSessionsByEventRequestHandler(
        IEventSessionRepository eventSessionRepository,
        IPrivateEventManagementReader? privateManagement = null)
    {
        _eventSessionRepository = eventSessionRepository;
        _privateManagement = privateManagement;
    }

    public async Task<List<EventSessionListDto>> QueryAsync(
        GetManagedSessionsByEventRequest request,
        CancellationToken cancellationToken)
    {
        var eventSessions = _privateManagement is null
            ? await _eventSessionRepository.GetSessionsByEvent(request.EventId, cancellationToken)
            : (await _privateManagement.ReadAsync(new(PrivateEventManagementReadKind.Program, request.EventId), cancellationToken))
                .Sessions.OrderBy(session => session.StartTime).ToList();
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
