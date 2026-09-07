using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventCustomProperties.Requests.Commands;

namespace Explore.Application.Features.EventCustomProperties.Authorization;

public sealed class UpdateEventCustomPropertyDefinitionAuthorizationContextEnricher(
    IEventCustomPropertyRepository repository,
    ITenantContext? tenantContext = null)
    : IAuthorizationContextEnricher<UpdateEventCustomPropertyDefinitionCommand>
{
    public async Task<AuthorizationContext> ResolveAsync(
        UpdateEventCustomPropertyDefinitionCommand request,
        CancellationToken cancellationToken)
    {
        var definition = await repository.GetDefinitionWithDetails(request.DefinitionId);
        if (definition is null || (tenantContext is not null && definition.TenantId != tenantContext.TenantId))
        {
            throw new AuthorizationException(ResourceKinds.Tenant, AuthorizationActions.Update);
        }

        return TenantContext(definition.TenantId);
    }

    private static AuthorizationContext TenantContext(Guid tenantId) =>
        new(tenantId.ToString(), new TenantScopedAuthorizationFacts(tenantId));
}
