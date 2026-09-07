using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventSessionAgendaItems.Requests.Commands;

namespace Explore.Application.Features.EventSessionAgendaItems.Authorization;

public sealed class UpdateEventSessionAgendaItemAuthorizationContextEnricher(
    IEventSessionAgendaItemRepository repository,
    ITenantContext? tenantContext = null)
    : IAuthorizationContextEnricher<UpdateEventSessionAgendaItemCommand>
{
    public async Task<AuthorizationContext> ResolveAsync(
        UpdateEventSessionAgendaItemCommand request,
        CancellationToken cancellationToken)
    {
        var assignment = await repository.GetByIdWithDetails(request.EventSessionAgendaItemId, cancellationToken);
        if (assignment?.EventSession is null ||
            assignment.EventSession.TenantId != assignment.TenantId ||
            (tenantContext is not null && assignment.TenantId != tenantContext.TenantId))
        {
            throw new AuthorizationException(ResourceKinds.EventSessionAgendaItem, AuthorizationActions.Update);
        }

        return new AuthorizationContext(
            assignment.Id.ToString(),
            new EventScopedAuthorizationFacts(
                assignment.TenantId,
                assignment.EventSession.EventId,
                assignment.EventSessionId));
    }
}
