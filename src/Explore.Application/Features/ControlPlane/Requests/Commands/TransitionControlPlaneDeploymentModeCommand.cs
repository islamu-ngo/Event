using Explore.Application.Authorization;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.ControlPlane.Requests.Commands;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update)]
public sealed record TransitionControlPlaneDeploymentModeCommand
    : IRequest<BaseCommandResponse<ControlPlaneDeploymentModeTransitionDto>>, ISecureRequest
{
    public TransitionControlPlaneDeploymentModeCommand(
        DeploymentMode targetMode,
        string? reason,
        string? confirmationText)
    {
        TargetMode = targetMode;
        Reason = reason;
        ConfirmationText = confirmationText;
    }

    public const string SettingKey = "control-plane.deployment-mode.runbook";

    public DeploymentMode TargetMode { get; }

    public string? Reason { get; }

    public string? ConfirmationText { get; }

    string ISecureRequest.ResourceId => SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
