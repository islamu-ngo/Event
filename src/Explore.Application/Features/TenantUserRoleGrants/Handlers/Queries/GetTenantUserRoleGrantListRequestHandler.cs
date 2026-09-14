using Explore.Application.Contracts.Operations;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.TenantUserRoleGrant;
using Explore.Application.Features.TenantUserRoleGrants.Requests.Queries;

namespace Explore.Application.Features.TenantUserRoleGrants.Handlers.Queries;

public class GetTenantUserRoleGrantListRequestHandler : IQueryHandler<GetTenantUserRoleGrantListRequest, List<TenantUserRoleGrantListDto>>
{
    private readonly ITenantUserRoleGrantRepository _tenantUserRoleGrantRepository;

    public GetTenantUserRoleGrantListRequestHandler(ITenantUserRoleGrantRepository tenantUserRoleGrantRepository)
    {
        _tenantUserRoleGrantRepository = tenantUserRoleGrantRepository;
    }

    public async Task<List<TenantUserRoleGrantListDto>> QueryAsync(GetTenantUserRoleGrantListRequest request, CancellationToken cancellationToken)
    {
        var tenantUserRoleGrants = await _tenantUserRoleGrantRepository.GetGrantsWithDetails();
        return tenantUserRoleGrants.Select(TenantUserRoleGrantMapper.ToListItem).ToList();
    }
}
