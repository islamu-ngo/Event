using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.ControlPlane.Handlers.Queries;

public sealed class GetControlPlaneTenantDetailsQueryHandler(
    ITenantRepository tenantRepository,
    ITenantLifecycleLogRepository lifecycleLogRepository)
    : IQueryHandler<GetControlPlaneTenantDetailsQuery, ControlPlaneTenantDetailDto?>
{
    public async Task<ControlPlaneTenantDetailDto?> QueryAsync(
        GetControlPlaneTenantDetailsQuery request,
        CancellationToken cancellationToken)
    {
        var tenant = await tenantRepository.GetById(request.TenantId);
        if (tenant is null)
        {
            return null;
        }

        var lifecycleLogs = await lifecycleLogRepository.GetByTenantIdAsync(request.TenantId, limit: 50, cancellationToken);

        return ControlPlaneTenantMapper.ToDetail(tenant, lifecycleLogs);
    }
}
