using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventAgendaItem;
using Explore.Application.Features.EventAgendaItems.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventAgendaItems.Handlers.Queries;

public sealed class GetManagedEventAgendaItemsByEventRequestHandler(
    IEventAgendaItemRepository repository,
    IPrivateEventManagementReader? privateManagement = null)
    : IQueryHandler<GetManagedEventAgendaItemsByEventRequest, List<EventAgendaItemListDto>>
{
    public async Task<List<EventAgendaItemListDto>> QueryAsync(
        GetManagedEventAgendaItemsByEventRequest request,
        CancellationToken cancellationToken)
    {
        var items = privateManagement is null ? await repository.GetByEventAsync(request.EventId, cancellationToken)
            : (await privateManagement.ReadAsync(new(PrivateEventManagementReadKind.Program, request.EventId), cancellationToken))
                .AgendaItems.OrderBy(item => item.SortOrder).ThenBy(item => item.StartTime).ToList();
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
