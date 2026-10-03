using Explore.Application.Authorization;
using Explore.Application.Features.InstanceAdmin.Plans;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.InstanceAdmin.Requests.Queries;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.View)]
public sealed record ValidateInstanceTenantPlanDraftQuery
    : IQuery<TenantPlanValidationResult>, ISecureRequest
{
    public ValidateInstanceTenantPlanDraftQuery(TenantPlanDraft draft)
    {
        Draft = draft;
    }

    public const string SettingKey = "control-plane.tenant-plans";

    public TenantPlanDraft Draft { get; }

    string? ISecureRequest.ResourceId => SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
