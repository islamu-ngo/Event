using Explore.Application.Authorization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.ControlPlane.Requests.Commands;

/// <summary>
/// Unlocks a tenant-scoped setting override for an explicit tenant, allowing the
/// tenant to edit it again. Used by instance administrators from the Control Plane.
/// </summary>
[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update)]
public sealed record UnlockControlPlaneTenantSettingCommand
    : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public UnlockControlPlaneTenantSettingCommand(Guid tenantId, string key)
    {
        TenantId = tenantId;
        Key = key;
    }

    public Guid TenantId { get; }
    public string Key { get; }

    public const string SettingKey = "control-plane.tenant-effective-configuration";

    string? ISecureRequest.ResourceId => SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
