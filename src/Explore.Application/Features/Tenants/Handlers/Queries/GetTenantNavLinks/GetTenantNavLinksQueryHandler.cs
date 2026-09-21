using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Operations;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Features.Tenants.Requests.Queries;

namespace Explore.Application.Features.Tenants.Handlers.Queries;

/// <summary>
/// Handler for GetTenantNavLinksQuery.
/// Retrieves all navigation links for the current tenant, ordered by display order.
/// </summary>
public class GetTenantNavLinksQueryHandler : IQueryHandler<GetTenantNavLinksQuery, List<TenantNavigationLinkDto>>
{
    private readonly ITenantNavigationLinkRepository _navigationLinkRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantLifecycleAccessService _lifecycle;

    public GetTenantNavLinksQueryHandler(
        ITenantNavigationLinkRepository navigationLinkRepository,
        ITenantContext tenantContext,
        ITenantLifecycleAccessService lifecycle)
    {
        _navigationLinkRepository = navigationLinkRepository;
        _tenantContext = tenantContext;
        _lifecycle = lifecycle;
    }

    public async Task<List<TenantNavigationLinkDto>> QueryAsync(GetTenantNavLinksQuery request, CancellationToken cancellationToken = default)
    {
        if (!await _lifecycle.IsPublicAsync(_tenantContext.TenantId, cancellationToken))
            return [];

        // Get all navigation links for the current tenant, ordered by Order property
        var navigationLinks = await _navigationLinkRepository.GetByTenantIdOrderedAsync(
            _tenantContext.TenantId,
            cancellationToken);

        return navigationLinks.Select(TenantMapper.ToNavigationLink).ToList();
    }
}
