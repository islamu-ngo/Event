using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSessionAgendaItem;
using Explore.Application.Features.EventSessionAgendaItems.Requests.Queries;

namespace Explore.Application.Features.EventSessionAgendaItems.Handlers.Queries;

public class GetAgendaItemsBySessionRequestHandler : IQueryHandler<GetAgendaItemsBySessionRequest, List<EventSessionAgendaItemListDto>>
{
    private readonly IEventSessionAgendaItemRepository _agendaItemRepository;
    private readonly IEventLocationDisclosureService _disclosureService;

    public GetAgendaItemsBySessionRequestHandler(
        IEventSessionAgendaItemRepository agendaItemRepository,
        IEventLocationDisclosureService disclosureService)
    {
        _agendaItemRepository = agendaItemRepository;
        _disclosureService = disclosureService;
    }

    public async Task<List<EventSessionAgendaItemListDto>> QueryAsync(GetAgendaItemsBySessionRequest query, CancellationToken cancellationToken = default)
    {
        var agendaItems = await _agendaItemRepository.GetPublicBySessionAsync(query.EventSessionId, cancellationToken);
        return await PublicEventSessionAgendaItemLocationProjector.ProjectAsync(
            agendaItems,
            _disclosureService,
            cancellationToken);
    }
}
