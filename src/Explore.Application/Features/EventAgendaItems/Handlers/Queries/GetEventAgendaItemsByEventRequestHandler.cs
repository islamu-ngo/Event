using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventAgendaItem;
using Explore.Application.Features.EventAgendaItems.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventAgendaItems.Handlers.Queries;

public class GetEventAgendaItemsByEventRequestHandler : IRequestHandler<GetEventAgendaItemsByEventRequest, List<EventAgendaItemListDto>>
{
    private readonly IEventAgendaItemRepository _eventAgendaItemRepository;
    private readonly IEventLocationDisclosureService _disclosureService;

    public GetEventAgendaItemsByEventRequestHandler(
        IEventAgendaItemRepository eventAgendaItemRepository,
        IEventLocationDisclosureService disclosureService)
    {
        _eventAgendaItemRepository = eventAgendaItemRepository;
        _disclosureService = disclosureService;
    }

    public async Task<List<EventAgendaItemListDto>> Handle(GetEventAgendaItemsByEventRequest request, CancellationToken cancellationToken)
    {
        var items = await _eventAgendaItemRepository.GetPublicByEventAsync(request.EventId, cancellationToken);
        return await PublicEventAgendaItemLocationProjector.ProjectAsync(
            items,
            _disclosureService,
            cancellationToken);
    }
}
