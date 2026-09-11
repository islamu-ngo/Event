using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSessionGroup;
using Explore.Application.Features.EventSessionGroups.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventSessionGroups.Handlers.Queries;

public class GetEventSessionGroupsByEventRequestHandler : IRequestHandler<GetEventSessionGroupsByEventRequest, List<EventSessionGroupListDto>>
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

    public async Task<List<EventSessionGroupListDto>> Handle(GetEventSessionGroupsByEventRequest request, CancellationToken cancellationToken)
    {
        var groups = await _eventSessionGroupRepository.GetPublicByEventAsync(request.EventId, cancellationToken);
        return await PublicEventSessionGroupLocationProjector.ProjectAsync(
            groups,
            _disclosureService,
            cancellationToken);
    }
}
