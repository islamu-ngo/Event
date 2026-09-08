using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSessionAgendaItem;
using Explore.Application.Features.EventSessionAgendaItems.Requests.Queries;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSessionAgendaItems.Handlers.Queries;

public class GetEventSessionAgendaItemListRequestHandler : IRequestHandler<GetEventSessionAgendaItemListRequest, PaginatedResult<EventSessionAgendaItemListDto>>
{
    private readonly IEventSessionAgendaItemRepository _agendaItemRepository;
    private readonly IMapper _mapper;
    private readonly IEventLocationDisclosureService _disclosureService;

    public GetEventSessionAgendaItemListRequestHandler(
        IEventSessionAgendaItemRepository agendaItemRepository,
        IMapper mapper,
        IEventLocationDisclosureService disclosureService)
    {
        _agendaItemRepository = agendaItemRepository;
        _mapper = mapper;
        _disclosureService = disclosureService;
    }

    public async Task<PaginatedResult<EventSessionAgendaItemListDto>> Handle(GetEventSessionAgendaItemListRequest request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = PaginatedResult<EventSessionAgendaItemListDto>.NormalizeParameters(request.PageNumber, request.PageSize);
        var (agendaItems, totalCount) = await _agendaItemRepository.GetPublicAgendaItemsWithDetailsPagedAsync(
            pageNumber,
            pageSize,
            cancellationToken);
        var dtos = await PublicEventSessionAgendaItemLocationProjector.ProjectAsync(
            agendaItems,
            _mapper,
            _disclosureService,
            cancellationToken);
        return PaginatedResult<EventSessionAgendaItemListDto>.Create(dtos, totalCount, pageNumber, pageSize);
    }
}
