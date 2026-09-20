using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSessionAgendaItem;
using Explore.Application.Features.EventSessionAgendaItems.Requests.Queries;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventSessionAgendaItems.Handlers.Queries;

public class GetEventSessionAgendaItemListRequestHandler : IQueryHandler<GetEventSessionAgendaItemListRequest, PaginatedResult<EventSessionAgendaItemListDto>>
{
    private readonly IEventSessionAgendaItemRepository _agendaItemRepository;
    private readonly IEventLocationDisclosureService _disclosureService;

    public GetEventSessionAgendaItemListRequestHandler(
        IEventSessionAgendaItemRepository agendaItemRepository,
        IEventLocationDisclosureService disclosureService)
    {
        _agendaItemRepository = agendaItemRepository;
        _disclosureService = disclosureService;
    }

    public async Task<PaginatedResult<EventSessionAgendaItemListDto>> QueryAsync(GetEventSessionAgendaItemListRequest query, CancellationToken cancellationToken = default)
    {
        var (pageNumber, pageSize) = PaginatedResult<EventSessionAgendaItemListDto>.NormalizeParameters(query.PageNumber, query.PageSize);
        var (agendaItems, totalCount) = await _agendaItemRepository.GetPublicAgendaItemsWithDetailsPagedAsync(
            pageNumber,
            pageSize,
            cancellationToken);
        var dtos = await PublicEventSessionAgendaItemLocationProjector.ProjectAsync(
            agendaItems,
            _disclosureService,
            cancellationToken);
        return PaginatedResult<EventSessionAgendaItemListDto>.Create(dtos, totalCount, pageNumber, pageSize);
    }
}
