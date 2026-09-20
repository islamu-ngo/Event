using Explore.Application.Authorization;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Notifications.Requests.Commands;

[AuthorizeResource(ResourceKinds.Group, AuthorizationActions.Update)]
public sealed record SetGroupNotificationPreferenceMuteCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid GroupId { get; init; }
    public bool IsMuted { get; init; }

    string? ISecureRequest.ResourceId => GroupId.ToString();
}
