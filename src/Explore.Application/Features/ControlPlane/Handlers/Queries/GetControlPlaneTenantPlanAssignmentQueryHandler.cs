using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.Features.ControlPlane;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.ControlPlane.Handlers.Queries;

public sealed class GetControlPlaneTenantPlanAssignmentQueryHandler(ITenantPlanRepository tenantPlanRepository)
    : IQueryHandler<GetControlPlaneTenantPlanAssignmentQuery, ControlPlaneTenantPlanAssignmentDto?>
{
    public async Task<ControlPlaneTenantPlanAssignmentDto?> QueryAsync(
        GetControlPlaneTenantPlanAssignmentQuery request,
        CancellationToken cancellationToken)
    {
        var assignment = await tenantPlanRepository.GetActiveAssignmentForTenantAsync(request.TenantId, cancellationToken);
        return assignment is null ? null : ControlPlaneTenantPlanMapper.ToAssignment(assignment);
    }
}
