using Explore.Application.Authorization;
using Explore.Application.DTOs.InstanceAdmin;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.InstanceAdmin.Requests.Queries;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.View)]
public sealed record GetInstanceDomainsQuery : IQuery<InstanceDomainOverviewDto>, ISecureRequest
{
    public const string SettingKey = "control-plane.domains";

    string? ISecureRequest.ResourceId => SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
