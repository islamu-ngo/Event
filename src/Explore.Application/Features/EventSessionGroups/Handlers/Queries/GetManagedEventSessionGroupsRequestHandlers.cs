using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionGroup;
using Explore.Application.Features.EventSessionGroups.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventSessionGroups.Handlers.Queries;

public sealed class GetManagedEventSessionGroupsByEventRequestHandler(
    IEventSessionGroupRepository repository,
    IMapper mapper)
    : IRequestHandler<GetManagedEventSessionGroupsByEventRequest, List<EventSessionGroupListDto>>
{
    public async Task<List<EventSessionGroupListDto>> Handle(
        GetManagedEventSessionGroupsByEventRequest request,
        CancellationToken cancellationToken)
    {
        var groups = await repository.GetActiveByEventAsync(request.EventId, cancellationToken);
        var dtos = mapper.Map<List<EventSessionGroupListDto>>(groups);
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
    IEventSessionGroupRepository repository,
    IMapper mapper)
    : IRequestHandler<GetManagedEventSessionGroupDetailRequest, EventSessionGroupDto?>
{
    public async Task<EventSessionGroupDto?> Handle(
        GetManagedEventSessionGroupDetailRequest request,
        CancellationToken cancellationToken)
    {
        var group = await repository.GetWithDetailsAsync(request.Id, cancellationToken);
        if (group?.EventId != request.EventId)
            return null;

        var dto = mapper.Map<EventSessionGroupDto>(group);
        dto.LocationId = group.LocationId;
        dto.LocationName = group.Location?.FullName;
        dto.RoomId = group.RoomId;
        dto.RoomName = group.Room?.Name;
        return dto;
    }
}
