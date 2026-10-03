using Explore.Application.Authorization;
using Explore.Application.DTOs.InstanceAdmin;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.InstanceAdmin.Requests.Queries;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.View)]
public sealed record GetInstanceTenantPlanAssignmentQuery
    : IQuery<InstanceTenantPlanAssignmentDto?>, ISecureRequest
{
    public GetInstanceTenantPlanAssignmentQuery(Guid tenantId)
    {
        TenantId = tenantId;
    }

    public const string SettingKey = "control-plane.tenant-plan-assignments";

    public Guid TenantId { get; }

    string? ISecureRequest.ResourceId => SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
