using Explore.Application.Authorization;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.ControlPlane.Requests.Queries;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.View)]
public sealed record GetControlPlaneTenantPlanDetailQuery
    : IQuery<ControlPlaneTenantPlanDetailDto?>, ISecureRequest
{
    public GetControlPlaneTenantPlanDetailQuery(string key)
    {
        Key = key;
    }

    public const string SettingKey = "control-plane.tenant-plans";

    public string Key { get; }

    string? ISecureRequest.ResourceId => SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
