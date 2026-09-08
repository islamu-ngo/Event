using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSessionGroup;
using Explore.Application.DTOs.Location;
using Explore.Application.Features.EventSessionGroups.Requests.Queries;
using Explore.Application.Services;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.EventSessionGroups.Handlers.Queries;

public class GetEventSessionGroupDetailRequestHandler : IRequestHandler<GetEventSessionGroupDetailRequest, EventSessionGroupDto?>
{
    private readonly IEventSessionGroupRepository _eventSessionGroupRepository;
    private readonly IMapper _mapper;
    private readonly IEventLocationDisclosureService _disclosureService;

    public GetEventSessionGroupDetailRequestHandler(
        IEventSessionGroupRepository eventSessionGroupRepository,
        IMapper mapper,
        IEventLocationDisclosureService disclosureService)
    {
        _eventSessionGroupRepository = eventSessionGroupRepository;
        _mapper = mapper;
        _disclosureService = disclosureService;
    }

    public async Task<EventSessionGroupDto?> Handle(GetEventSessionGroupDetailRequest request, CancellationToken cancellationToken)
    {
        var group = await _eventSessionGroupRepository.GetPublicWithDetailsAsync(request.Id, cancellationToken);
        return await PublicEventSessionGroupLocationProjector.ProjectAsync(
            group,
            _mapper,
            _disclosureService,
            cancellationToken);
    }
}

internal static class PublicEventSessionGroupLocationProjector
{
    public static async Task<EventSessionGroupDto?> ProjectAsync(
        EventSessionGroup? group,
        IMapper mapper,
        IEventLocationDisclosureService disclosureService,
        CancellationToken cancellationToken)
    {
        if (group is null)
        {
            return null;
        }

        IReadOnlyDictionary<Guid, EventLocationPublicDto> locations =
            await PublicEventLocationProjection.ResolveAsync(
                disclosureService,
                [Placement(group)],
                cancellationToken);
        EventSessionGroupDto dto = mapper.Map<EventSessionGroupDto>(group);
        ClearLegacyLocation(dto);
        dto.EventLocation = group.EventLocationId is { } eventLocationId
            ? locations.GetValueOrDefault(eventLocationId)
            : null;
        return dto;
    }

    public static async Task<List<EventSessionGroupListDto>> ProjectAsync(
        IReadOnlyCollection<EventSessionGroup> groups,
        IMapper mapper,
        IEventLocationDisclosureService disclosureService,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<Guid, EventLocationPublicDto> locations =
            await PublicEventLocationProjection.ResolveAsync(
                disclosureService,
                groups.Select(Placement),
                cancellationToken);
        List<EventSessionGroupListDto> dtos = mapper.Map<List<EventSessionGroupListDto>>(groups);
        IReadOnlyDictionary<Guid, EventSessionGroup> groupById = groups.ToDictionary(group => group.Id);
        foreach (EventSessionGroupListDto dto in dtos)
        {
            ClearLegacyLocation(dto);
            EventSessionGroup group = groupById[dto.Id];
            dto.EventLocation = group.EventLocationId is { } eventLocationId
                ? locations.GetValueOrDefault(eventLocationId)
                : null;
        }

        return dtos;
    }

    private static PublicEventLocationPlacement Placement(EventSessionGroup group)
        => new(group.TenantId, group.EventId, group.EventLocationId, group.RoomId);

    private static void ClearLegacyLocation(EventSessionGroupDto dto)
    {
        dto.LocationId = null;
        dto.LocationName = null;
        dto.RoomId = null;
        dto.RoomName = null;
    }

    private static void ClearLegacyLocation(EventSessionGroupListDto dto)
    {
        dto.LocationId = null;
        dto.LocationName = null;
        dto.RoomId = null;
        dto.RoomName = null;
    }
}
