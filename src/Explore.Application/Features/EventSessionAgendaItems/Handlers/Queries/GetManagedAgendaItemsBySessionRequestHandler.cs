using Explore.Application.Contracts.Operations;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionAgendaItem;
using Explore.Application.Features.EventSessionAgendaItems.Requests.Queries;

namespace Explore.Application.Features.EventSessionAgendaItems.Handlers.Queries;

public sealed class GetManagedAgendaItemsBySessionRequestHandler(
    IEventSessionRepository sessionRepository,
    IEventSessionAgendaItemRepository agendaItemRepository)
    : IQueryHandler<GetManagedAgendaItemsBySessionRequest, List<EventSessionAgendaItemListDto>?>
{
    public async Task<List<EventSessionAgendaItemListDto>?> QueryAsync(
        GetManagedAgendaItemsBySessionRequest query,
        CancellationToken cancellationToken = default)
    {
        var session = await sessionRepository.GetSessionWithDetails(query.EventSessionId);
        if (session?.EventId != query.EventId)
            return null;

        var items = await agendaItemRepository.GetBySession(query.EventSessionId, cancellationToken);
        var dtos = items.Select(EventSessionMapper.ToListItem).ToList();
        for (var index = 0; index < dtos.Count; index++)
            dtos[index].LocationFullName = items[index].Location?.FullName;

        return dtos;
    }
}
