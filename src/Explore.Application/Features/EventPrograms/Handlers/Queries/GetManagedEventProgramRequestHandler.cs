using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventProgram;
using Explore.Application.Features.EventPrograms.Requests.Queries;
using Explore.Application.Mappings;

namespace Explore.Application.Features.EventPrograms.Handlers.Queries;

public sealed class GetManagedEventProgramRequestHandler(IPrivateEventManagementReader reader)
    : IQueryHandler<GetManagedEventProgramRequest, ManagedEventProgramDto>
{
    public async Task<ManagedEventProgramDto> QueryAsync(
        GetManagedEventProgramRequest request, CancellationToken cancellationToken)
    {
        var graph = await reader.ReadAsync(
            new(PrivateEventManagementReadKind.Program, request.EventId), cancellationToken);
        var sessions = graph.Sessions.OrderBy(session => session.StartTime).Select(session =>
        {
            var dto = EventSessionMapper.ToListItem(session);
            dto.LocationId = session.LocationId;
            dto.LocationFullName = session.Location?.FullName;
            dto.LocationCity = session.Location?.City;
            dto.RoomId = session.RoomId;
            dto.RoomName = session.Room?.Name;
            return dto;
        }).ToList();
        var groups = graph.SessionGroups.OrderBy(group => group.SortOrder).ThenBy(group => group.Name)
            .Select(group =>
            {
                var dto = EventSessionMapper.ToListItem(group);
                dto.LocationId = group.LocationId;
                dto.LocationName = group.Location?.FullName;
                dto.RoomId = group.RoomId;
                dto.RoomName = group.Room?.Name;
                return dto;
            }).ToList();
        var days = graph.Days.OrderBy(day => day.SortOrder).ThenBy(day => day.LocalDate)
            .Select(EventMapper.ToListItem).ToList();
        var items = graph.AgendaItems.OrderBy(item => item.SortOrder).ThenBy(item => item.StartTime)
            .Select(EventMapper.ToListItem).ToList();
        return new(sessions, groups, days, items);
    }
}
