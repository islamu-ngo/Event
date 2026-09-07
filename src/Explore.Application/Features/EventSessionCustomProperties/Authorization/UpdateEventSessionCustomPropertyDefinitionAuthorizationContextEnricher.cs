using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventSessionCustomProperties.Requests.Commands;

namespace Explore.Application.Features.EventSessionCustomProperties.Authorization;

public sealed class UpdateEventSessionCustomPropertyDefinitionAuthorizationContextEnricher(
    IEventSessionCustomPropertyRepository repository,
    ITenantContext? tenantContext = null)
    : IAuthorizationContextEnricher<UpdateEventSessionCustomPropertyDefinitionCommand>
{
    public async Task<AuthorizationContext> ResolveAsync(
        UpdateEventSessionCustomPropertyDefinitionCommand request,
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
