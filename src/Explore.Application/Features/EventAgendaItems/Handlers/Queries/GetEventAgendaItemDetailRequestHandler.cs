using AutoMapper;
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
    private readonly IMapper _mapper;
    private readonly IEventLocationDisclosureService _disclosureService;

    public GetEventAgendaItemDetailRequestHandler(
        IEventAgendaItemRepository eventAgendaItemRepository,
        IMapper mapper,
        IEventLocationDisclosureService disclosureService)
    {
        _eventAgendaItemRepository = eventAgendaItemRepository;
        _mapper = mapper;
        _disclosureService = disclosureService;
    }

    public async Task<EventAgendaItemDto?> Handle(GetEventAgendaItemDetailRequest request, CancellationToken cancellationToken)
    {
        var agendaItem = await _eventAgendaItemRepository.GetPublicByIdAsync(request.Id, cancellationToken);
        return await PublicEventAgendaItemLocationProjector.ProjectAsync(
            agendaItem,
            _mapper,
            _disclosureService,
            cancellationToken);
    }
}

internal static class PublicEventAgendaItemLocationProjector
{
    public static async Task<EventAgendaItemDto?> ProjectAsync(
        EventAgendaItem? item,
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
        EventAgendaItemDto dto = mapper.Map<EventAgendaItemDto>(item);
        dto.LocationId = null;
        dto.RoomId = null;
        dto.EventLocation = item.EventLocationId is { } eventLocationId
            ? locations.GetValueOrDefault(eventLocationId)
            : null;
        return dto;
    }

    public static async Task<List<EventAgendaItemListDto>> ProjectAsync(
        IReadOnlyCollection<EventAgendaItem> items,
        IMapper mapper,
        IEventLocationDisclosureService disclosureService,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<Guid, EventLocationPublicDto> locations =
            await PublicEventLocationProjection.ResolveAsync(
                disclosureService,
                items.Select(Placement),
                cancellationToken);
        List<EventAgendaItemListDto> dtos = mapper.Map<List<EventAgendaItemListDto>>(items);
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
