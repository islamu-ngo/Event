using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.InstanceAdmin;
using Explore.Application.Features.InstanceAdmin;
using Explore.Application.Features.InstanceAdmin.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.InstanceAdmin.Handlers.Queries;

public sealed class GetInstanceTenantPlanAssignmentQueryHandler(ITenantPlanRepository tenantPlanRepository)
    : IQueryHandler<GetInstanceTenantPlanAssignmentQuery, InstanceTenantPlanAssignmentDto?>
{
    public async Task<InstanceTenantPlanAssignmentDto?> QueryAsync(
        GetInstanceTenantPlanAssignmentQuery request,
        CancellationToken cancellationToken)
    {
        var assignment = await tenantPlanRepository.GetActiveAssignmentForTenantAsync(request.TenantId, cancellationToken);
        return assignment is null ? null : InstanceTenantPlanMapper.ToAssignment(assignment);
    }
}
