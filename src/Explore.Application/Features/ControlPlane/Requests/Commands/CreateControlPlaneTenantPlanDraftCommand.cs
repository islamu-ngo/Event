using Explore.Application.Authorization;
using Explore.Application.Features.ControlPlane.Plans;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.ControlPlane.Requests.Commands;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update)]
public sealed record CreateControlPlaneTenantPlanDraftCommand
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public CreateControlPlaneTenantPlanDraftCommand(TenantPlanDraft draft)
    {
        Draft = draft;
    }

    public const string SettingKey = "control-plane.tenant-plans";

    public TenantPlanDraft Draft { get; }

    string? ISecureRequest.ResourceId => SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
