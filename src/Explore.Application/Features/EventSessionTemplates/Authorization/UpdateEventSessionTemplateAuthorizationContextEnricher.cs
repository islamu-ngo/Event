using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventSessionTemplates.Requests.Commands;

namespace Explore.Application.Features.EventSessionTemplates.Authorization;

public sealed class UpdateEventSessionTemplateAuthorizationContextEnricher(
    IEventSessionTemplateRepository repository,
    ITenantContext? tenantContext = null)
    : IAuthorizationContextEnricher<UpdateEventSessionTemplateCommand>
{
    public async Task<AuthorizationContext> ResolveAsync(
        UpdateEventSessionTemplateCommand request,
        CancellationToken cancellationToken)
    {
        var template = await repository.GetSessionTemplateWithDetails(request.SessionTemplateId);
        if (template is null || (tenantContext is not null && template.TenantId != tenantContext.TenantId))
        {
            throw new AuthorizationException(ResourceKinds.Tenant, AuthorizationActions.Update);
        }

        return TenantContext(template.TenantId);
    }

    private static AuthorizationContext TenantContext(Guid tenantId) =>
        new(tenantId.ToString(), new TenantScopedAuthorizationFacts(tenantId));
}
