using Explore.Application.Authorization;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.ControlPlane.Requests.Commands;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update)]
public sealed record RollbackControlPlaneTenantPlanAssignmentCommand
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public RollbackControlPlaneTenantPlanAssignmentCommand(
        Guid tenantId,
        Guid assignmentId,
        Guid operatorId)
    {
        TenantId = tenantId;
        AssignmentId = assignmentId;
        OperatorId = operatorId;
    }

    public const string SettingKey = "control-plane.tenant-plan-assignments";

    public Guid TenantId { get; }
    public Guid AssignmentId { get; }
    public Guid OperatorId { get; }

    string? ISecureRequest.ResourceId => SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
