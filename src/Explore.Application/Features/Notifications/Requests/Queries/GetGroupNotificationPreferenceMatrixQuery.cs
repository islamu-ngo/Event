using Explore.Application.Authorization;
using Explore.Application.DTOs.Notification;
using MediatR;

namespace Explore.Application.Features.Notifications.Requests.Queries;

[AuthorizeResource(ResourceKinds.Group, AuthorizationActions.View)]
public sealed record GetGroupNotificationPreferenceMatrixQuery : IRequest<NotificationPreferenceMatrixDto>, ISecureRequest
{
    public Guid GroupId { get; init; }

    string? ISecureRequest.ResourceId => GroupId.ToString();
}
