using Explore.Application.Authorization;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.ControlPlane.Requests.Queries;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.View)]
public sealed record GetControlPlaneTenantEffectiveConfigurationQuery
    : IQuery<ControlPlaneTenantEffectiveConfigurationDto>, ISecureRequest
{
    public GetControlPlaneTenantEffectiveConfigurationQuery(Guid tenantId)
    {
        TenantId = tenantId;
    }

    public const string SettingKey = "control-plane.tenant-effective-configuration";

    public Guid TenantId { get; }

    string? ISecureRequest.ResourceId => SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
