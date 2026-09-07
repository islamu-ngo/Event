using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.ControlPlane.Handlers.Queries;

public sealed class GetControlPlaneTenantDetailsQueryHandler(
    ITenantRepository tenantRepository,
    ITenantLifecycleLogRepository lifecycleLogRepository)
    : IRequestHandler<GetControlPlaneTenantDetailsQuery, ControlPlaneTenantDetailDto?>
{
    public async Task<ControlPlaneTenantDetailDto?> Handle(
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
