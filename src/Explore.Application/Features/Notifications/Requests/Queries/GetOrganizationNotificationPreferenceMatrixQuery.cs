using Explore.Application.Authorization;
using Explore.Application.DTOs.Notification;
using MediatR;

namespace Explore.Application.Features.Notifications.Requests.Queries;

[AuthorizeResource(ResourceKinds.Organization, AuthorizationActions.View)]
public sealed record GetOrganizationNotificationPreferenceMatrixQuery : IRequest<NotificationPreferenceMatrixDto>, ISecureRequest
{
    public Guid OrganizationId { get; init; }

    string? ISecureRequest.ResourceId => OrganizationId.ToString();
}
