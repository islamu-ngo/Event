using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSession;
using Explore.Application.Features.EventSessions.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventSessions.Handlers.Queries;

public class GetSessionsByEventRequestHandler : IRequestHandler<GetSessionsByEventRequest, List<EventSessionListDto>>
{
    private readonly IEventSessionRepository _eventSessionRepository;
    private readonly IEventLocationDisclosureService _disclosureService;

    public GetSessionsByEventRequestHandler(
        IEventSessionRepository eventSessionRepository,
        IEventLocationDisclosureService disclosureService)
    {
        _eventSessionRepository = eventSessionRepository;
        _disclosureService = disclosureService;
    }

    public async Task<List<EventSessionListDto>> Handle(GetSessionsByEventRequest request, CancellationToken cancellationToken)
    {
        var eventSessions = await _eventSessionRepository.GetPublicSessionsByEventAsync(
            request.EventId,
            cancellationToken);
        return await PublicEventSessionLocationProjector.ProjectAsync(
            eventSessions,
            _disclosureService,
            cancellationToken);
    }
}
