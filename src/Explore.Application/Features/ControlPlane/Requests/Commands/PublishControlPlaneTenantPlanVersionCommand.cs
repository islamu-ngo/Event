using Explore.Application.Authorization;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.ControlPlane.Requests.Commands;

public enum TenantPlanExistingAssignmentPolicy
{
    LeaveExistingTenantsPinned = 0,
    MoveExistingTenantsToPublishedVersion = 1
}

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update)]
public sealed record PublishControlPlaneTenantPlanVersionCommand
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public PublishControlPlaneTenantPlanVersionCommand(
        Guid versionId,
        TenantPlanExistingAssignmentPolicy existingTenantPolicy)
    {
        VersionId = versionId;
        ExistingTenantPolicy = existingTenantPolicy;
    }

    public const string SettingKey = "control-plane.tenant-plans";

    public Guid VersionId { get; }
    public TenantPlanExistingAssignmentPolicy ExistingTenantPolicy { get; }

    string? ISecureRequest.ResourceId => SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
