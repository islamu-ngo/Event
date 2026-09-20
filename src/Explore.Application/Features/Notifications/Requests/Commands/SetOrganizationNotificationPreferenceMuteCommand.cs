using Explore.Application.Authorization;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Notifications.Requests.Commands;

[AuthorizeResource(ResourceKinds.Organization, AuthorizationActions.Update)]
public sealed record SetOrganizationNotificationPreferenceMuteCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid OrganizationId { get; init; }
    public bool IsMuted { get; init; }

    string? ISecureRequest.ResourceId => OrganizationId.ToString();
}
