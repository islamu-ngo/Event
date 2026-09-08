using Explore.Application.Contracts.Services;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services;

public class TenantResolverService : ITenantResolverService
{
    private static readonly Guid FallbackDefaultTenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");

    private readonly DeploymentSettings _deploymentSettings;
    private readonly ITenantContextAccessor _tenantContextAccessor;
    private readonly IReadOnlyList<ITenantResolver> _tenantResolvers;

    public TenantResolverService(
        IEnumerable<ITenantResolver> tenantResolvers,
        ITenantContextAccessor tenantContextAccessor,
        IOptions<DeploymentSettings> deploymentSettings)
    {
        _tenantResolvers = tenantResolvers.OrderBy(resolver => resolver.Priority).ToArray();
        _tenantContextAccessor = tenantContextAccessor;
        _deploymentSettings = deploymentSettings.Value;
    }

    public Guid ResolveTenantId()
    {
        if (_tenantContextAccessor.TenantId is Guid cachedTenantId)
        {
            return cachedTenantId;
        }

        foreach (var tenantResolver in _tenantResolvers)
        {
            if (tenantResolver.ResolveTenantId() is not Guid resolvedTenantId || resolvedTenantId == Guid.Empty)
            {
                continue;
            }

            _tenantContextAccessor.SetTenant(resolvedTenantId);
            return resolvedTenantId;
        }

        if (_deploymentSettings.IsSingleTenant)
        {
            var fallbackTenantId = _deploymentSettings.DefaultTenantId != Guid.Empty
                ? _deploymentSettings.DefaultTenantId
                : FallbackDefaultTenantId;

            _tenantContextAccessor.SetTenant(fallbackTenantId);
            return fallbackTenantId;
        }

        return Guid.Empty;
    }
}
