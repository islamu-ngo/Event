using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSession;
using Explore.Application.Features.EventSessions.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventSessions.Handlers.Queries;

public sealed class GetEventSessionAuthorizationContextRequestHandler
    : IRequestHandler<GetEventSessionAuthorizationContextRequest, EventSessionAuthorizationContextDto?>
{
    private readonly IEventSessionRepository _eventSessionRepository;

    public GetEventSessionAuthorizationContextRequestHandler(IEventSessionRepository eventSessionRepository)
    {
        _eventSessionRepository = eventSessionRepository;
    }

    public async Task<EventSessionAuthorizationContextDto?> Handle(
        GetEventSessionAuthorizationContextRequest request,
        CancellationToken cancellationToken)
    {
        var session = await _eventSessionRepository.GetSessionWithDetails(request.EventSessionId);
        if (session is null)
        {
            return null;
        }

        return new EventSessionAuthorizationContextDto
        {
            Id = session.Id,
            EventId = session.EventId,
            TenantId = session.TenantId
        };
    }
}
