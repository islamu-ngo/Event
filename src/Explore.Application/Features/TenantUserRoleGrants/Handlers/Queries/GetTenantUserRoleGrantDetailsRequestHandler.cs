using Explore.Application.Contracts.Operations;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.TenantUserRoleGrant;
using Explore.Application.Features.TenantUserRoleGrants.Requests.Queries;

namespace Explore.Application.Features.TenantUserRoleGrants.Handlers.Queries;

public class GetTenantUserRoleGrantDetailsRequestHandler : IQueryHandler<GetTenantUserRoleGrantDetailsRequest, TenantUserRoleGrantDto?>
{
    private readonly ITenantUserRoleGrantRepository _tenantUserRoleGrantRepository;
    private readonly IHumanAccountAdministrativeActionPreparation _preparation;
    private readonly ISerializableAttemptOwner _owner;

    public GetTenantUserRoleGrantDetailsRequestHandler(ITenantUserRoleGrantRepository tenantUserRoleGrantRepository,
        IHumanAccountAdministrativeActionPreparation preparation, ISerializableAttemptOwner owner)
    {
        _tenantUserRoleGrantRepository = tenantUserRoleGrantRepository;
        _preparation = preparation;
        _owner = owner;
    }

    public async Task<TenantUserRoleGrantDto?> QueryAsync(GetTenantUserRoleGrantDetailsRequest request, CancellationToken cancellationToken)
    {
        var prepared = await _preparation.PrepareAsync(request, cancellationToken);
        return await _owner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(request, token);
            return await QueryBodyAsync(request);
        }, cancellationToken);
    }

    private async Task<TenantUserRoleGrantDto?> QueryBodyAsync(GetTenantUserRoleGrantDetailsRequest request)
    {
        var tenantUserRoleGrant = await _tenantUserRoleGrantRepository.GetGrantWithDetails(request.Id);
        if (tenantUserRoleGrant == null)
        {
            return null;
        }

        return TenantUserRoleGrantMapper.ToDetail(tenantUserRoleGrant);
    }
}
