using Explore.Application.Authorization;
using Explore.Application.DTOs.InstanceAdmin;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.InstanceAdmin.Requests.Queries;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.View)]
public sealed record GetInstanceTenantPlanListQuery : IQuery<IReadOnlyList<InstanceTenantPlanListItemDto>>, ISecureRequest
{
    public const string SettingKey = "control-plane.tenant-plans";

    string? ISecureRequest.ResourceId => SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
