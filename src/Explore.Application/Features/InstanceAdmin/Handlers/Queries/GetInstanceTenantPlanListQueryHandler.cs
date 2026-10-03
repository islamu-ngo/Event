using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.InstanceAdmin;
using Explore.Application.Features.InstanceAdmin;
using Explore.Application.Features.InstanceAdmin.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.InstanceAdmin.Handlers.Queries;

public sealed class GetInstanceTenantPlanListQueryHandler(ITenantPlanRepository tenantPlanRepository)
    : IQueryHandler<GetInstanceTenantPlanListQuery, IReadOnlyList<InstanceTenantPlanListItemDto>>
{
    public async Task<IReadOnlyList<InstanceTenantPlanListItemDto>> QueryAsync(
        GetInstanceTenantPlanListQuery request,
        CancellationToken cancellationToken)
    {
        _ = request;
        var plans = await tenantPlanRepository.ListWithVersionsAsync(cancellationToken);

        return plans
            .OrderBy(plan => plan.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(InstanceTenantPlanMapper.ToListItem)
            .ToArray();
    }
}
