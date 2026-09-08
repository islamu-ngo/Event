using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionAgendaItem;
using Explore.Application.Features.EventSessionAgendaItems.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventSessionAgendaItems.Handlers.Queries;

public sealed class GetManagedAgendaItemsBySessionRequestHandler(
    IEventSessionRepository sessionRepository,
    IEventSessionAgendaItemRepository agendaItemRepository,
    IMapper mapper)
    : IRequestHandler<GetManagedAgendaItemsBySessionRequest, List<EventSessionAgendaItemListDto>?>
{
    public async Task<List<EventSessionAgendaItemListDto>?> Handle(
        GetManagedAgendaItemsBySessionRequest request,
        CancellationToken cancellationToken)
    {
        var session = await sessionRepository.GetSessionWithDetails(request.EventSessionId);
        if (session?.EventId != request.EventId)
            return null;

        var items = await agendaItemRepository.GetBySession(request.EventSessionId, cancellationToken);
        var dtos = mapper.Map<List<EventSessionAgendaItemListDto>>(items);
        for (var index = 0; index < dtos.Count; index++)
            dtos[index].LocationFullName = items[index].Location?.FullName;

        return dtos;
    }
}
