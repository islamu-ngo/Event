using System.Collections.Generic;
using Explore.Application.Contracts.Operations;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Features.Tenants.Requests.Queries;

namespace Explore.Application.Features.Tenants.Handlers.Queries;

public class GetTenantListRequestHandler : IQueryHandler<GetTenantListRequest, List<TenantListDto>>
{
    private readonly ITenantRepository _tenantRepository;

    public GetTenantListRequestHandler(ITenantRepository tenantRepository)
    {
        _tenantRepository = tenantRepository;
    }

    public async Task<List<TenantListDto>> QueryAsync(GetTenantListRequest request, CancellationToken cancellationToken = default)
    {
        var tenants = await _tenantRepository.GetAll();
        return tenants.Select(TenantMapper.ToListItem).ToList();
    }
}
