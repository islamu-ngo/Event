using Explore.Application.Authorization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EmailDispatch.Requests.Commands;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update)]
public sealed record SetEmailDispatchGlobalRateLimitOverrideCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public int? RateLimitPerMinute { get; init; }
    public Guid? ChangedBy { get; init; }

    string ISecureRequest.ResourceId => EmailDispatchProcessorControl.SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
