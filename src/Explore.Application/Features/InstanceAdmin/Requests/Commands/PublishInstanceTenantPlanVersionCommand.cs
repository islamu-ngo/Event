using Explore.Application.Authorization;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.InstanceAdmin.Requests.Commands;

public enum TenantPlanExistingAssignmentPolicy
{
    LeaveExistingTenantsPinned = 0,
    MoveExistingTenantsToPublishedVersion = 1
}

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update)]
public sealed record PublishInstanceTenantPlanVersionCommand
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public PublishInstanceTenantPlanVersionCommand(
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
