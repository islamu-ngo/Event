using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.Features.ControlPlane;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.ControlPlane.Handlers.Queries;

public sealed class GetControlPlaneTenantPlanListQueryHandler(ITenantPlanRepository tenantPlanRepository)
    : IRequestHandler<GetControlPlaneTenantPlanListQuery, IReadOnlyList<ControlPlaneTenantPlanListItemDto>>
{
    public async Task<IReadOnlyList<ControlPlaneTenantPlanListItemDto>> Handle(
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
