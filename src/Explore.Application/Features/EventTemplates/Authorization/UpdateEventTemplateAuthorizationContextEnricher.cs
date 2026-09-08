using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventTemplates.Requests.Commands;

namespace Explore.Application.Features.EventTemplates.Authorization;

public sealed class UpdateEventTemplateAuthorizationContextEnricher(
    IEventTemplateRepository repository,
    ITenantContext? tenantContext = null)
    : IAuthorizationContextEnricher<UpdateEventTemplateCommand>
{
    public async Task<AuthorizationContext> ResolveAsync(
        UpdateEventTemplateCommand request,
        CancellationToken cancellationToken)
    {
        var template = await repository.GetTemplateWithDetails(request.TemplateId);
        if (template is null || (tenantContext is not null && template.TenantId != tenantContext.TenantId))
        {
            throw new AuthorizationException(ResourceKinds.Tenant, AuthorizationActions.Update);
        }

        return TenantContext(template.TenantId);
    }

    private static AuthorizationContext TenantContext(Guid tenantId) =>
        new(tenantId.ToString(), new TenantScopedAuthorizationFacts(tenantId));
}
