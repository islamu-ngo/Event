using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;

namespace Explore.Infrastructure.Services;

public class TenantContext : ITenantContext
{
    private readonly ITenantContextAccessor _tenantContextAccessor;
    private readonly ITenantResolverService _tenantResolverService;

    public TenantContext(
        ITenantContextAccessor tenantContextAccessor,
        ITenantResolverService tenantResolverService)
    {
        _tenantContextAccessor = tenantContextAccessor;
        _tenantResolverService = tenantResolverService;
    }

    public Guid TenantId => _tenantContextAccessor.TenantId ?? _tenantResolverService.ResolveTenantId();
}
