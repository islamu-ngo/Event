using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSession;
using Explore.Application.Features.EventSessionGroups.Requests.Queries;
using Explore.Application.Features.EventSessions.Handlers.Queries;

namespace Explore.Application.Features.EventSessionGroups.Handlers.Queries;

public class GetEventSessionGroupSessionsRequestHandler : IQueryHandler<GetEventSessionGroupSessionsRequest, List<EventSessionListDto>>
{
    private readonly IEventSessionGroupRepository _eventSessionGroupRepository;
    private readonly IEventSessionGroupSessionRepository _assignmentRepository;
    private readonly IEventLocationDisclosureService _disclosureService;

    public GetEventSessionGroupSessionsRequestHandler(
        IEventSessionGroupRepository eventSessionGroupRepository,
        IEventSessionGroupSessionRepository assignmentRepository,
        IEventLocationDisclosureService disclosureService)
    {
        _eventSessionGroupRepository = eventSessionGroupRepository;
        _assignmentRepository = assignmentRepository;
        _disclosureService = disclosureService;
    }

    public async Task<List<EventSessionListDto>> QueryAsync(
        GetEventSessionGroupSessionsRequest query,
        CancellationToken cancellationToken = default)
    {
        var group = await _eventSessionGroupRepository.GetPublicWithDetailsAsync(query.EventSessionGroupId, cancellationToken);
        if (group is null)
        {
            return [];
        }

        var assignments = await _assignmentRepository.GetPublicByGroupAsync(query.EventSessionGroupId, cancellationToken);
        var sessions = assignments.Select(assignment => assignment.EventSession).ToList();

        return await PublicEventSessionLocationProjector.ProjectAsync(
            sessions,
            _disclosureService,
            cancellationToken);
    }
}
