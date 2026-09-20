using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Localization.Requests.Queries;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Localization.Handlers.Queries;

public sealed class GetLocalizationTmsApiKeyConfiguredQueryHandler(
    ITenantContext tenantContext,
    ISecretBindingRepository secretBindingRepository)
    : IQueryHandler<GetLocalizationTmsApiKeyConfiguredQuery, bool>
{
    public async Task<bool> QueryAsync(
        GetLocalizationTmsApiKeyConfiguredQuery request,
        CancellationToken cancellationToken)
    {
        var tenantBindingExists = await secretBindingRepository.ExistsForScopeAsync(
            SecretDefinitionRegistry.Keys.Localization.TmsApiKey,
            SecretScope.Tenant,
            tenantContext.TenantId,
            cancellationToken);
        if (tenantBindingExists)
            return true;

        return await secretBindingRepository.ExistsForScopeAsync(
            SecretDefinitionRegistry.Keys.Localization.TmsApiKey,
            SecretScope.Instance,
            null,
            cancellationToken);
    }
}
