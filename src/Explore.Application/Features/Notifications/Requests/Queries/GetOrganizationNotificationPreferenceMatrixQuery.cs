using Explore.Application.Authorization;
using Explore.Application.DTOs.Notification;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Notifications.Requests.Queries;

[AuthorizeResource(ResourceKinds.Organization, AuthorizationActions.View)]
public sealed record GetOrganizationNotificationPreferenceMatrixQuery : IQuery<NotificationPreferenceMatrixDto>, ISecureRequest
{
    public Guid OrganizationId { get; init; }

    string? ISecureRequest.ResourceId => OrganizationId.ToString();
}
