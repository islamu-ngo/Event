using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Features.Tenants.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Tenants.Handlers.Queries;

/// <summary>
/// Handler for GetTenantNavLinksQuery.
/// Retrieves all navigation links for the current tenant, ordered by display order.
/// </summary>
public class GetTenantNavLinksQueryHandler : IRequestHandler<GetTenantNavLinksQuery, List<TenantNavigationLinkDto>>
{
    private readonly ITenantNavigationLinkRepository _navigationLinkRepository;
    private readonly ITenantContext _tenantContext;

    public GetTenantNavLinksQueryHandler(
        ITenantNavigationLinkRepository navigationLinkRepository,
        ITenantContext tenantContext)
    {
        _navigationLinkRepository = navigationLinkRepository;
        _tenantContext = tenantContext;
    }

    public async Task<List<TenantNavigationLinkDto>> Handle(GetTenantNavLinksQuery request, CancellationToken cancellationToken)
    {
        // Get all navigation links for the current tenant, ordered by Order property
        var navigationLinks = await _navigationLinkRepository.GetByTenantIdOrderedAsync(
            _tenantContext.TenantId,
            cancellationToken);

        return navigationLinks.Select(TenantMapper.ToNavigationLink).ToList();
    }
}
