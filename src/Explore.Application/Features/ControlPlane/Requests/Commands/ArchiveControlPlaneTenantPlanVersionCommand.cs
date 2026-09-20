using Explore.Application.Authorization;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.ControlPlane.Requests.Commands;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update)]
public sealed record ArchiveControlPlaneTenantPlanVersionCommand
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public ArchiveControlPlaneTenantPlanVersionCommand(Guid versionId)
    {
        VersionId = versionId;
    }

    public const string SettingKey = "control-plane.tenant-plans";

    public Guid VersionId { get; }

    string? ISecureRequest.ResourceId => SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
