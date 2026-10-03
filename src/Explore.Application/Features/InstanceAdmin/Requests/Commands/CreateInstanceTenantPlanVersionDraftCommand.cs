using Explore.Application.Authorization;
using Explore.Application.Features.InstanceAdmin.Plans;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.InstanceAdmin.Requests.Commands;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update)]
public sealed record CreateInstanceTenantPlanVersionDraftCommand
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public CreateInstanceTenantPlanVersionDraftCommand(string planKey, TenantPlanDraft draft)
    {
        PlanKey = planKey;
        Draft = draft;
    }

    public const string SettingKey = "control-plane.tenant-plans";

    public string PlanKey { get; }
    public TenantPlanDraft Draft { get; }

    string? ISecureRequest.ResourceId => SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
