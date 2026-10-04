using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.InstanceAdmin;
using Explore.Application.Features.InstanceAdmin;
using Explore.Application.Features.InstanceAdmin.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.InstanceAdmin.Handlers.Queries;

public sealed class GetInstanceTenantPlanDetailQueryHandler(ITenantPlanRepository tenantPlanRepository)
    : IQueryHandler<GetInstanceTenantPlanDetailQuery, InstanceTenantPlanDetailDto?>
{
    public async Task<InstanceTenantPlanDetailDto?> QueryAsync(
        GetInstanceTenantPlanDetailQuery request,
        CancellationToken cancellationToken)
    {
        var plan = await tenantPlanRepository.GetByKeyAsync(request.Key, cancellationToken);
        return plan is null ? null : InstanceTenantPlanMapper.ToDetail(plan);
    }
}
