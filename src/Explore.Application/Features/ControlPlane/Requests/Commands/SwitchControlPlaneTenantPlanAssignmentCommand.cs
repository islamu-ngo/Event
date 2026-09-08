using Explore.Application.Authorization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.ControlPlane.Requests.Commands;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update)]
public sealed record SwitchControlPlaneTenantPlanAssignmentCommand
    : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public SwitchControlPlaneTenantPlanAssignmentCommand(
        Guid tenantId,
        Guid tenantPlanVersionId,
        Guid assignedByUserId)
    {
        TenantId = tenantId;
        TenantPlanVersionId = tenantPlanVersionId;
        AssignedByUserId = assignedByUserId;
    }

    public const string SettingKey = "control-plane.tenant-plan-assignments";

    public Guid TenantId { get; }
    public Guid TenantPlanVersionId { get; }
    public Guid AssignedByUserId { get; }

    string? ISecureRequest.ResourceId => SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
