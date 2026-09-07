using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventAgendaItem;
using Explore.Application.Features.EventAgendaItems.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventAgendaItems.Handlers.Queries;

public sealed class GetManagedEventAgendaItemsByEventRequestHandler(
    IEventAgendaItemRepository repository,
    IMapper mapper)
    : IRequestHandler<GetManagedEventAgendaItemsByEventRequest, List<EventAgendaItemListDto>>
{
    public async Task<List<EventAgendaItemListDto>> Handle(
        GetManagedEventAgendaItemsByEventRequest request,
        CancellationToken cancellationToken)
    {
        var items = await repository.GetByEventAsync(request.EventId, cancellationToken);
        return mapper.Map<List<EventAgendaItemListDto>>(items);
    }
}

public sealed class GetManagedEventAgendaItemDetailRequestHandler(
    IEventAgendaItemRepository repository,
    IMapper mapper)
    : IRequestHandler<GetManagedEventAgendaItemDetailRequest, EventAgendaItemDto?>
{
    public async Task<EventAgendaItemDto?> Handle(
        GetManagedEventAgendaItemDetailRequest request,
        CancellationToken cancellationToken)
    {
        var item = await repository.GetById(request.Id);
        if (item?.EventId != request.EventId)
            return null;

        var dto = mapper.Map<EventAgendaItemDto>(item);
        dto.LocationId = item.LocationId;
        dto.RoomId = item.RoomId;
        return dto;
    }
}
