using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionGroup;
using Explore.Application.Features.EventSessionGroups.Requests.Queries;
using Explore.Application.Mappings;

namespace Explore.Application.Features.EventSessionGroups.Handlers.Queries;

public sealed class GetManagedEventSessionGroupsByEventRequestHandler(
    IEventSessionGroupRepository repository)
    : IQueryHandler<GetManagedEventSessionGroupsByEventRequest, List<EventSessionGroupListDto>>
{
    public async Task<List<EventSessionGroupListDto>> QueryAsync(
        GetManagedEventSessionGroupsByEventRequest request,
        CancellationToken cancellationToken = default)
    {
        var groups = await repository.GetActiveByEventAsync(request.EventId, cancellationToken);
        var dtos = groups.Select(EventSessionMapper.ToListItem).ToList();
        for (var index = 0; index < dtos.Count; index++)
        {
            var group = groups[index];
            var dto = dtos[index];
            dto.LocationId = group.LocationId;
            dto.LocationName = group.Location?.FullName;
            dto.RoomId = group.RoomId;
            dto.RoomName = group.Room?.Name;
        }

        return dtos;
    }
}

public sealed class GetManagedEventSessionGroupDetailRequestHandler(
    IEventSessionGroupRepository repository)
    : IQueryHandler<GetManagedEventSessionGroupDetailRequest, EventSessionGroupDto?>
{
    public async Task<EventSessionGroupDto?> QueryAsync(
        GetManagedEventSessionGroupDetailRequest request,
        CancellationToken cancellationToken = default)
    {
        var group = await repository.GetWithDetailsAsync(request.Id, cancellationToken);
        if (group?.EventId != request.EventId)
            return null;

        var dto = EventSessionMapper.ToDetail(group);
        dto.LocationId = group.LocationId;
        dto.LocationName = group.Location?.FullName;
        dto.RoomId = group.RoomId;
        dto.RoomName = group.Room?.Name;
        return dto;
    }
}

