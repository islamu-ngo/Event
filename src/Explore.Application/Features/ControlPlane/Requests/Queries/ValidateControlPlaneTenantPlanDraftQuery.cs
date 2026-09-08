using Explore.Application.Authorization;
using Explore.Application.Features.ControlPlane.Plans;
using MediatR;

namespace Explore.Application.Features.ControlPlane.Requests.Queries;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.View)]
public sealed record ValidateControlPlaneTenantPlanDraftQuery
    : IRequest<TenantPlanValidationResult>, ISecureRequest
{
    public ValidateControlPlaneTenantPlanDraftQuery(TenantPlanDraft draft)
    {
        Draft = draft;
    }

    public const string SettingKey = "control-plane.tenant-plans";

    public TenantPlanDraft Draft { get; }

    string? ISecureRequest.ResourceId => SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
