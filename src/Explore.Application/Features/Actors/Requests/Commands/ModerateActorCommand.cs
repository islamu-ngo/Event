using Explore.Application.Authorization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Actors.Requests.Commands;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update)]
public sealed record ModerateActorCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public const string SettingKey = "global-actor-moderation";

    public Guid ActorId { get; init; }
    public GlobalModerationRequest? Moderation { get; init; }

    string? ISecureRequest.ResourceId => SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
