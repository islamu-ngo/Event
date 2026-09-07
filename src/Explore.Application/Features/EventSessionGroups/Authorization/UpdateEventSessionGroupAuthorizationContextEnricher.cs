using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventSessionGroups.Requests.Commands;

namespace Explore.Application.Features.EventSessionGroups.Authorization;

public sealed class UpdateEventSessionGroupAuthorizationContextEnricher(
    IEventSessionGroupRepository repository,
    ITenantContext? tenantContext = null)
    : IAuthorizationContextEnricher<UpdateEventSessionGroupCommand>
{
    public async Task<AuthorizationContext> ResolveAsync(
        UpdateEventSessionGroupCommand request,
        CancellationToken cancellationToken)
    {
        var assignment = await repository.GetForUpdateAsync(request.EventSessionGroupId, cancellationToken);
        if (assignment is null || (tenantContext is not null && assignment.TenantId != tenantContext.TenantId))
        {
            throw new AuthorizationException(ResourceKinds.EventSessionGroup, AuthorizationActions.Update);
        }

        return new AuthorizationContext(
            assignment.Id.ToString(),
            new EventScopedAuthorizationFacts(assignment.TenantId, assignment.EventId));
    }
}
