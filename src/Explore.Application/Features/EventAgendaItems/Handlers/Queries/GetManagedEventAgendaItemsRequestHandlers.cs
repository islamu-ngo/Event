using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventAgendaItem;
using Explore.Application.Features.EventAgendaItems.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventAgendaItems.Handlers.Queries;

public sealed class GetManagedEventAgendaItemsByEventRequestHandler(
    IEventAgendaItemRepository repository)
    : IRequestHandler<GetManagedEventAgendaItemsByEventRequest, List<EventAgendaItemListDto>>
{
    public async Task<List<EventAgendaItemListDto>> Handle(
        GetManagedEventAgendaItemsByEventRequest request,
        CancellationToken cancellationToken)
    {
        var items = await repository.GetByEventAsync(request.EventId, cancellationToken);
        return items.Select(EventMapper.ToListItem).ToList();
    }
}

public sealed class GetManagedEventAgendaItemDetailRequestHandler(
    IEventAgendaItemRepository repository)
    : IRequestHandler<GetManagedEventAgendaItemDetailRequest, EventAgendaItemDto?>
{
    public async Task<EventAgendaItemDto?> Handle(
        GetManagedEventAgendaItemDetailRequest request,
        CancellationToken cancellationToken)
    {
        var item = await repository.GetById(request.Id);
        if (item?.EventId != request.EventId)
            return null;

        var dto = EventMapper.ToDetail(item);
        dto.LocationId = item.LocationId;
        dto.RoomId = item.RoomId;
        return dto;
    }
}
