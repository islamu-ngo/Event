using Explore.Application.Authorization;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.InstanceAdmin.Requests.Commands;

/// <summary>
/// Writes or updates a tenant-scoped setting override for an explicit tenant from
/// instance administration. Bypasses the current-tenant context used by the regular
/// tenant settings endpoints, so instance administrators can govern any tenant.
/// </summary>
[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update)]
public sealed record SetInstanceTenantSettingCommand
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public SetInstanceTenantSettingCommand(Guid tenantId, string key, string value)
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
