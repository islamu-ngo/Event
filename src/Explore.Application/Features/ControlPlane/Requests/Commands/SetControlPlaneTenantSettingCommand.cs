using Explore.Application.Authorization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.ControlPlane.Requests.Commands;

/// <summary>
/// Writes or updates a tenant-scoped setting override for an explicit tenant from
/// the Control Plane. Bypasses the current-tenant context used by the regular
/// tenant settings endpoints, so instance administrators can govern any tenant.
/// </summary>
[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update)]
public sealed record SetControlPlaneTenantSettingCommand
    : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public SetControlPlaneTenantSettingCommand(Guid tenantId, string key, string value)
    {
        TenantId = tenantId;
        Key = key;
        Value = value;
    }

    public Guid TenantId { get; }
    public string Key { get; }
    public string Value { get; }

    public const string SettingKey = "control-plane.tenant-effective-configuration";

    string? ISecureRequest.ResourceId => SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
