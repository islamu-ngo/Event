using Explore.Application.Authorization;
using Explore.Application.DTOs.InstanceAdmin;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.InstanceAdmin.Requests.Commands;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update)]
public sealed record TransitionInstanceDeploymentModeCommand
    : ICommand<BaseCommandResponse<InstanceDeploymentModeTransitionDto>>, ISecureRequest
{
    public TransitionInstanceDeploymentModeCommand(
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
