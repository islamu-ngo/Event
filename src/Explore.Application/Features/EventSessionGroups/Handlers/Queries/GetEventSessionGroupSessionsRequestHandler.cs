using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSession;
using Explore.Application.Features.EventSessionGroups.Requests.Queries;
using Explore.Application.Features.EventSessions.Handlers.Queries;
using MediatR;

namespace Explore.Application.Features.EventSessionGroups.Handlers.Queries;

public class GetEventSessionGroupSessionsRequestHandler : IRequestHandler<GetEventSessionGroupSessionsRequest, List<EventSessionListDto>>
{
    private readonly IEventSessionGroupRepository _eventSessionGroupRepository;
    private readonly IEventSessionGroupSessionRepository _assignmentRepository;
    private readonly IMapper _mapper;
    private readonly IEventLocationDisclosureService _disclosureService;

    public GetEventSessionGroupSessionsRequestHandler(
        IEventSessionGroupRepository eventSessionGroupRepository,
        IEventSessionGroupSessionRepository assignmentRepository,
        IMapper mapper,
        IEventLocationDisclosureService disclosureService)
    {
        _eventSessionGroupRepository = eventSessionGroupRepository;
        _assignmentRepository = assignmentRepository;
        _mapper = mapper;
        _disclosureService = disclosureService;
    }

    public async Task<List<EventSessionListDto>> Handle(
        GetEventSessionGroupSessionsRequest request,
        CancellationToken cancellationToken)
    {
        var group = await _eventSessionGroupRepository.GetPublicWithDetailsAsync(request.EventSessionGroupId, cancellationToken);
        if (group is null)
        {
            return [];
        }

        var assignments = await _assignmentRepository.GetPublicByGroupAsync(request.EventSessionGroupId, cancellationToken);
        var sessions = assignments.Select(assignment => assignment.EventSession).ToList();

        return await PublicEventSessionLocationProjector.ProjectAsync(
            sessions,
            _mapper,
            _disclosureService,
            cancellationToken);
    }
}
