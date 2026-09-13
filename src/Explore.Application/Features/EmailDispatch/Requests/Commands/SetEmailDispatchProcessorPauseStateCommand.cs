using Explore.Application.Authorization;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EmailDispatch.Requests.Commands;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update)]
public sealed record SetEmailDispatchProcessorPauseStateCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public bool IsPaused { get; init; }
    public string? PauseReason { get; init; }
    public Guid? ChangedBy { get; init; }

    string ISecureRequest.ResourceId => EmailDispatchProcessorControl.SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
