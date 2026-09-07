using Explore.Application.Authorization;
using Explore.Application.Features.ControlPlane.Plans;
using MediatR;

namespace Explore.Application.Features.ControlPlane.Requests.Queries;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.View)]
public sealed record PreviewControlPlaneTenantPlanDiffQuery
    : IRequest<TenantPlanDiffResult>, ISecureRequest
{
    public PreviewControlPlaneTenantPlanDiffQuery(
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
