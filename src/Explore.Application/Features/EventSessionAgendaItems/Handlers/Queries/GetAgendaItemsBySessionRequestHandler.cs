using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSessionAgendaItem;
using Explore.Application.Features.EventSessionAgendaItems.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventSessionAgendaItems.Handlers.Queries;

public class GetAgendaItemsBySessionRequestHandler : IRequestHandler<GetAgendaItemsBySessionRequest, List<EventSessionAgendaItemListDto>>
{
    private readonly IEventSessionAgendaItemRepository _agendaItemRepository;
    private readonly IMapper _mapper;
    private readonly IEventLocationDisclosureService _disclosureService;

    public GetAgendaItemsBySessionRequestHandler(
        IEventSessionAgendaItemRepository agendaItemRepository,
        IMapper mapper,
        IEventLocationDisclosureService disclosureService)
    {
        _agendaItemRepository = agendaItemRepository;
        _mapper = mapper;
        _disclosureService = disclosureService;
    }

    public async Task<List<EventSessionAgendaItemListDto>> Handle(GetAgendaItemsBySessionRequest request, CancellationToken cancellationToken)
    {
        var agendaItems = await _agendaItemRepository.GetPublicBySessionAsync(request.EventSessionId, cancellationToken);
        return await PublicEventSessionAgendaItemLocationProjector.ProjectAsync(
            agendaItems,
            _mapper,
            _disclosureService,
            cancellationToken);
    }
}
