using Explore.Application.Contracts.Operations;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.TenantUserRoleGrant;
using Explore.Application.Features.TenantUserRoleGrants.Requests.Queries;

namespace Explore.Application.Features.TenantUserRoleGrants.Handlers.Queries;

public class GetTenantUserRoleGrantDetailsRequestHandler : IQueryHandler<GetTenantUserRoleGrantDetailsRequest, TenantUserRoleGrantDto?>
{
    private readonly ITenantUserRoleGrantRepository _tenantUserRoleGrantRepository;

    public GetTenantUserRoleGrantDetailsRequestHandler(ITenantUserRoleGrantRepository tenantUserRoleGrantRepository)
    {
        _tenantUserRoleGrantRepository = tenantUserRoleGrantRepository;
    }

    public async Task<TenantUserRoleGrantDto?> QueryAsync(GetTenantUserRoleGrantDetailsRequest request, CancellationToken cancellationToken)
    {
        var tenantUserRoleGrant = await _tenantUserRoleGrantRepository.GetGrantWithDetails(request.Id);
        if (tenantUserRoleGrant == null)
        {
            return null;
        }

        return TenantUserRoleGrantMapper.ToDetail(tenantUserRoleGrant);
    }
}
