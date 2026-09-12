using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventAgendaItem;
using Explore.Application.DTOs.Location;
using Explore.Application.Features.EventAgendaItems.Requests.Queries;
using Explore.Application.Services;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.EventAgendaItems.Handlers.Queries;

public class GetEventAgendaItemDetailRequestHandler : IRequestHandler<GetEventAgendaItemDetailRequest, EventAgendaItemDto?>
{
    private readonly IEventAgendaItemRepository _eventAgendaItemRepository;
    private readonly IEventLocationDisclosureService _disclosureService;

    public GetEventAgendaItemDetailRequestHandler(
        IEventAgendaItemRepository eventAgendaItemRepository,
        IEventLocationDisclosureService disclosureService)
    {
        _eventAgendaItemRepository = eventAgendaItemRepository;
        _disclosureService = disclosureService;
    }

    public async Task<EventAgendaItemDto?> Handle(GetEventAgendaItemDetailRequest request, CancellationToken cancellationToken)
    {
        var agendaItem = await _eventAgendaItemRepository.GetPublicByIdAsync(request.Id, cancellationToken);
        return await PublicEventAgendaItemLocationProjector.ProjectAsync(
            agendaItem,
            _disclosureService,
            cancellationToken);
    }
}

internal static class PublicEventAgendaItemLocationProjector
{
    public static async Task<EventAgendaItemDto?> ProjectAsync(
        EventAgendaItem? item,
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
        EventAgendaItemDto dto = EventMapper.ToDetail(item);
        dto.LocationId = null;
        dto.RoomId = null;
        dto.EventLocation = item.EventLocationId is { } eventLocationId
            ? locations.GetValueOrDefault(eventLocationId)
            : null;
        return dto;
    }

    public static async Task<List<EventAgendaItemListDto>> ProjectAsync(
        IReadOnlyCollection<EventAgendaItem> items,
        IEventLocationDisclosureService disclosureService,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<Guid, EventLocationPublicDto> locations =
            await PublicEventLocationProjection.ResolveAsync(
                disclosureService,
                items.Select(Placement),
                cancellationToken);
        List<EventAgendaItemListDto> dtos = items.Select(EventMapper.ToListItem).ToList();
        IReadOnlyDictionary<Guid, EventAgendaItem> itemById = items.ToDictionary(item => item.Id);
        foreach (EventAgendaItemListDto dto in dtos)
        {
            EventAgendaItem item = itemById[dto.Id];
            dto.EventLocation = item.EventLocationId is { } eventLocationId
                ? locations.GetValueOrDefault(eventLocationId)
                : null;
        }

        return dtos;
    }

    private static PublicEventLocationPlacement Placement(EventAgendaItem item)
        => new(item.TenantId, item.EventId, item.EventLocationId, item.RoomId);
}
