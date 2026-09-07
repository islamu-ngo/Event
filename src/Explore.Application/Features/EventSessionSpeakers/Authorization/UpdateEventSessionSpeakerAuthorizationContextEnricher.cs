using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventSessionSpeakers.Requests.Commands;

namespace Explore.Application.Features.EventSessionSpeakers.Authorization;

public sealed class UpdateEventSessionSpeakerAuthorizationContextEnricher(
    IEventSessionSpeakerRepository speakerRepository,
    IEventSessionRepository eventSessionRepository,
    ITenantContext? tenantContext = null)
    : IAuthorizationContextEnricher<UpdateEventSessionSpeakerCommand>
{
    public async Task<AuthorizationContext> ResolveAsync(
        UpdateEventSessionSpeakerCommand request,
        CancellationToken cancellationToken)
    {
        var assignment = await speakerRepository.GetById(request.EventSessionSpeakerId);
        var session = assignment is null
            ? null
            : await eventSessionRepository.GetSessionWithDetails(assignment.EventSessionId);
        if (assignment is null ||
            session is null ||
            assignment.TenantId != session.TenantId ||
            (tenantContext is not null && assignment.TenantId != tenantContext.TenantId))
        {
            throw new AuthorizationException(ResourceKinds.EventSession, AuthorizationActions.Update);
        }

        return new AuthorizationContext(
            session.Id.ToString(),
            new EventScopedAuthorizationFacts(session.TenantId, session.EventId, session.Id));
    }
}
