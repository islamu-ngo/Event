using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.Features.ControlPlane;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.ControlPlane.Handlers.Queries;

public sealed class GetControlPlaneTenantPlanAssignmentQueryHandler(ITenantPlanRepository tenantPlanRepository)
    : IRequestHandler<GetControlPlaneTenantPlanAssignmentQuery, ControlPlaneTenantPlanAssignmentDto?>
{
    public async Task<ControlPlaneTenantPlanAssignmentDto?> Handle(
        GetControlPlaneTenantPlanAssignmentQuery request,
        CancellationToken cancellationToken)
    {
        var assignment = await tenantPlanRepository.GetActiveAssignmentForTenantAsync(request.TenantId, cancellationToken);
        return assignment is null ? null : ControlPlaneTenantPlanMapper.ToAssignment(assignment);
    }
}
