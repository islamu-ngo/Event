using Explore.Application.Contracts.Operations;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.TenantUserRoleGrant;
using Explore.Application.Features.TenantUserRoleGrants.Requests.Queries;

namespace Explore.Application.Features.TenantUserRoleGrants.Handlers.Queries;

public class GetTenantUserRoleGrantListRequestHandler : IQueryHandler<GetTenantUserRoleGrantListRequest, List<TenantUserRoleGrantListDto>>
{
    private readonly ITenantUserRoleGrantRepository _tenantUserRoleGrantRepository;
    private readonly IHumanAccountAdministrativeActionPreparation _preparation;
    private readonly ISerializableAttemptOwner _owner;

    public GetTenantUserRoleGrantListRequestHandler(ITenantUserRoleGrantRepository tenantUserRoleGrantRepository,
        IHumanAccountAdministrativeActionPreparation preparation, ISerializableAttemptOwner owner)
    {
        _tenantUserRoleGrantRepository = tenantUserRoleGrantRepository;
        _preparation = preparation;
        _owner = owner;
    }

    public async Task<List<TenantUserRoleGrantListDto>> QueryAsync(GetTenantUserRoleGrantListRequest request, CancellationToken cancellationToken)
    {
        var prepared = await _preparation.PrepareAsync(request, cancellationToken);
        return await _owner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(request, token);
            var tenantUserRoleGrants = await _tenantUserRoleGrantRepository.GetGrantsWithDetails();
            return tenantUserRoleGrants.Select(TenantUserRoleGrantMapper.ToListItem).ToList();
        }, cancellationToken);
    }
}
