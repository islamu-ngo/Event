using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSessionGroup;
using Explore.Application.Features.EventSessionGroups.Requests.Queries;

namespace Explore.Application.Features.EventSessionGroups.Handlers.Queries;

public class GetEventSessionGroupsByEventRequestHandler : IQueryHandler<GetEventSessionGroupsByEventRequest, List<EventSessionGroupListDto>>
{
    private readonly IEventSessionGroupRepository _eventSessionGroupRepository;
    private readonly IEventLocationDisclosureService _disclosureService;

    public GetEventSessionGroupsByEventRequestHandler(
        IEventSessionGroupRepository eventSessionGroupRepository,
        IEventLocationDisclosureService disclosureService)
    {
        _eventSessionGroupRepository = eventSessionGroupRepository;
        _disclosureService = disclosureService;
    }

    public async Task<List<EventSessionGroupListDto>> QueryAsync(GetEventSessionGroupsByEventRequest query, CancellationToken cancellationToken = default)
    {
        var groups = await _eventSessionGroupRepository.GetPublicByEventAsync(query.EventId, cancellationToken);
        return await PublicEventSessionGroupLocationProjector.ProjectAsync(
            groups,
            _disclosureService,
            cancellationToken);
    }
}
