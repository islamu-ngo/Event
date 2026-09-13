using Explore.Application.Authorization;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.ControlPlane.Requests.Queries;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.View)]
public sealed record GetControlPlaneOverviewQuery : IQuery<ControlPlaneOverviewDto>, ISecureRequest
{
    public const string SettingKey = "control-plane";

    string? ISecureRequest.ResourceId => SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
