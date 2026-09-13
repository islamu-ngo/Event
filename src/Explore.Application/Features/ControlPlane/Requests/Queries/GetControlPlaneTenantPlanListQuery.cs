using Explore.Application.Authorization;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.ControlPlane.Requests.Queries;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.View)]
public sealed record GetControlPlaneTenantPlanListQuery : IQuery<IReadOnlyList<ControlPlaneTenantPlanListItemDto>>, ISecureRequest
{
    public const string SettingKey = "control-plane.tenant-plans";

    string? ISecureRequest.ResourceId => SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
