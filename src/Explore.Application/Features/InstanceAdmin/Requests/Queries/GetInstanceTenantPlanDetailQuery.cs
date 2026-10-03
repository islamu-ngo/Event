using Explore.Application.Authorization;
using Explore.Application.DTOs.InstanceAdmin;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.InstanceAdmin.Requests.Queries;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.View)]
public sealed record GetInstanceTenantPlanDetailQuery
    : IQuery<InstanceTenantPlanDetailDto?>, ISecureRequest
{
    public GetInstanceTenantPlanDetailQuery(string key)
    {
        Key = key;
    }

    public const string SettingKey = "control-plane.tenant-plans";

    public string Key { get; }

    string? ISecureRequest.ResourceId => SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
