using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.Features.ControlPlane;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.ControlPlane.Handlers.Queries;

public sealed class GetControlPlaneTenantPlanListQueryHandler(ITenantPlanRepository tenantPlanRepository)
    : IQueryHandler<GetControlPlaneTenantPlanListQuery, IReadOnlyList<ControlPlaneTenantPlanListItemDto>>
{
    public async Task<IReadOnlyList<ControlPlaneTenantPlanListItemDto>> QueryAsync(
        GetControlPlaneTenantPlanListQuery request,
        CancellationToken cancellationToken)
    {
        _ = request;
        var plans = await tenantPlanRepository.ListWithVersionsAsync(cancellationToken);

        return plans
            .OrderBy(plan => plan.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(ControlPlaneTenantPlanMapper.ToListItem)
            .ToArray();
    }
}
