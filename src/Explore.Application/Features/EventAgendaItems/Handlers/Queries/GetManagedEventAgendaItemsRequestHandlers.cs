using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventAgendaItem;
using Explore.Application.Features.EventAgendaItems.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventAgendaItems.Handlers.Queries;

public sealed class GetManagedEventAgendaItemsByEventRequestHandler(
    IEventAgendaItemRepository repository)
    : IQueryHandler<GetManagedEventAgendaItemsByEventRequest, List<EventAgendaItemListDto>>
{
    public async Task<List<EventAgendaItemListDto>> QueryAsync(
        GetManagedEventAgendaItemsByEventRequest request,
        CancellationToken cancellationToken)
    {
        var items = await repository.GetByEventAsync(request.EventId, cancellationToken);
        return items.Select(EventMapper.ToListItem).ToList();
    }
}

public sealed class GetManagedEventAgendaItemDetailRequestHandler(
    IEventAgendaItemRepository repository)
    : IQueryHandler<GetManagedEventAgendaItemDetailRequest, EventAgendaItemDto?>
{
    public async Task<EventAgendaItemDto?> QueryAsync(
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
