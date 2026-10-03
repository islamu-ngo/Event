using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.InstanceAdmin;
using Explore.Application.Features.InstanceAdmin.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.InstanceAdmin.Handlers.Queries;

public sealed class GetInstanceTenantDetailsQueryHandler(
    ITenantRepository tenantRepository,
    ITenantLifecycleLogRepository lifecycleLogRepository)
    : IQueryHandler<GetInstanceTenantDetailsQuery, InstanceTenantDetailDto?>
{
    public async Task<InstanceTenantDetailDto?> QueryAsync(
        GetInstanceTenantDetailsQuery request,
        CancellationToken cancellationToken)
    {
        var tenant = await tenantRepository.GetById(request.TenantId);
        if (tenant is null)
        {
            return null;
        }

        var lifecycleLogs = await lifecycleLogRepository.GetByTenantIdAsync(request.TenantId, limit: 50, cancellationToken);

        return InstanceTenantMapper.ToDetail(tenant, lifecycleLogs);
    }
}
