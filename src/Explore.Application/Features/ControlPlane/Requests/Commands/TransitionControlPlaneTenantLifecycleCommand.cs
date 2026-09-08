using Explore.Application.Authorization;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.ControlPlane.Requests.Commands;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update)]
public sealed record TransitionControlPlaneTenantLifecycleCommand
    : IRequest<BaseCommandResponse<ControlPlaneTenantLifecycleTransitionDto>>, ISecureRequest
{
    public TransitionControlPlaneTenantLifecycleCommand(
        Guid tenantId,
        TenantStatusEnum targetStatus,
        string? reason,
        string? confirmationText = null)
    {
        TenantId = tenantId;
        TargetStatus = targetStatus;
        Reason = reason;
        ConfirmationText = confirmationText;
    }

    public const string SettingKey = "control-plane.tenants.lifecycle";

    public Guid TenantId { get; }
    public TenantStatusEnum TargetStatus { get; }
    public string? Reason { get; }
    public string? ConfirmationText { get; }

    string? ISecureRequest.ResourceId => SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
