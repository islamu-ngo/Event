using Explore.Application.Authorization;
using Explore.Application.Features.InstanceAdmin.Plans;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.InstanceAdmin.Requests.Queries;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.View)]
public sealed record PreviewInstanceTenantPlanDiffQuery
    : IQuery<TenantPlanDiffResult>, ISecureRequest
{
    public PreviewInstanceTenantPlanDiffQuery(
        TenantPlanEffectiveConfiguration current,
        TenantPlanDraft draft)
    {
        Current = current;
        Draft = draft;
    }

    public const string SettingKey = "control-plane.tenant-plan-assignments";

    public TenantPlanEffectiveConfiguration Current { get; }
    public TenantPlanDraft Draft { get; }

    string? ISecureRequest.ResourceId => SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
