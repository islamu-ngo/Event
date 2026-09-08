using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSessionAgendaItem;
using Explore.Application.DTOs.Location;
using Explore.Application.Features.EventSessionAgendaItems.Requests.Queries;
using Explore.Application.Services;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.EventSessionAgendaItems.Handlers.Queries;

public class GetEventSessionAgendaItemDetailsRequestHandler : IRequestHandler<GetEventSessionAgendaItemDetailsRequest, EventSessionAgendaItemDto?>
{
    private readonly IEventSessionAgendaItemRepository _agendaItemRepository;
    private readonly IMapper _mapper;
    private readonly IEventLocationDisclosureService _disclosureService;

    public GetEventSessionAgendaItemDetailsRequestHandler(
        IEventSessionAgendaItemRepository agendaItemRepository,
        IMapper mapper,
        IEventLocationDisclosureService disclosureService)
    {
        _agendaItemRepository = agendaItemRepository;
        _mapper = mapper;
        _disclosureService = disclosureService;
    }

    public async Task<EventSessionAgendaItemDto?> Handle(GetEventSessionAgendaItemDetailsRequest request, CancellationToken cancellationToken)
    {
        var agendaItem = await _agendaItemRepository.GetPublicByIdWithDetailsAsync(request.Id, cancellationToken);
        return await PublicEventSessionAgendaItemLocationProjector.ProjectAsync(
            agendaItem,
            _mapper,
            _disclosureService,
            cancellationToken);
    }
}

internal static class PublicEventSessionAgendaItemLocationProjector
{
    public static async Task<EventSessionAgendaItemDto?> ProjectAsync(
        EventSessionAgendaItem? item,
        IMapper mapper,
        IEventLocationDisclosureService disclosureService,
        CancellationToken cancellationToken)
    {
        if (item is null)
        {
            return null;
        }

        IReadOnlyDictionary<Guid, EventLocationPublicDto> locations =
            await PublicEventLocationProjection.ResolveAsync(
                disclosureService,
                [Placement(item)],
                cancellationToken);
        EventSessionAgendaItemDto dto = mapper.Map<EventSessionAgendaItemDto>(item);
        dto.LocationId = null;
        dto.LocationFullName = null;
        dto.EventLocation = item.EventLocationId is { } eventLocationId
            ? locations.GetValueOrDefault(eventLocationId)
            : null;
        return dto;
    }

    public static async Task<List<EventSessionAgendaItemListDto>> ProjectAsync(
        IReadOnlyCollection<EventSessionAgendaItem> items,
        IMapper mapper,
        IEventLocationDisclosureService disclosureService,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<Guid, EventLocationPublicDto> locations =
            await PublicEventLocationProjection.ResolveAsync(
                disclosureService,
                items.Select(Placement),
                cancellationToken);
        List<EventSessionAgendaItemListDto> dtos = mapper.Map<List<EventSessionAgendaItemListDto>>(items);
        IReadOnlyDictionary<Guid, EventSessionAgendaItem> itemById = items.ToDictionary(item => item.Id);
        foreach (EventSessionAgendaItemListDto dto in dtos)
        {
            dto.LocationFullName = null;
            EventSessionAgendaItem item = itemById[dto.Id];
            dto.EventLocation = item.EventLocationId is { } eventLocationId
                ? locations.GetValueOrDefault(eventLocationId)
                : null;
        }

        return dtos;
    }

    private static PublicEventLocationPlacement Placement(EventSessionAgendaItem item)
        => new(
            item.TenantId,
            item.EventSession.EventId,
            item.EventLocationId,
            RoomId: null);
}
