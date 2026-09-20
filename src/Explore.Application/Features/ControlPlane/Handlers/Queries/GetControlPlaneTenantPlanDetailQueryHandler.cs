using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.Features.ControlPlane;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.ControlPlane.Handlers.Queries;

public sealed class GetControlPlaneTenantPlanDetailQueryHandler(ITenantPlanRepository tenantPlanRepository)
    : IQueryHandler<GetControlPlaneTenantPlanDetailQuery, ControlPlaneTenantPlanDetailDto?>
{
    public async Task<ControlPlaneTenantPlanDetailDto?> QueryAsync(
        GetControlPlaneTenantPlanDetailQuery request,
        CancellationToken cancellationToken)
    {
        var plan = await tenantPlanRepository.GetByKeyAsync(request.Key, cancellationToken);
        return plan is null ? null : ControlPlaneTenantPlanMapper.ToDetail(plan);
    }
}
