using Explore.Application.Authorization;
using Explore.Application.DTOs.Webhooks;
using MediatR;

namespace Explore.Application.Features.Webhooks.Requests.Queries;

[AuthorizeResource(ResourceKinds.Webhook, AuthorizationActions.Webhooks.ViewDelivery)]
public sealed record GetWebhookDeliveryAttemptByIdQuery
    : IRequest<WebhookDeliveryAttemptDto?>, ISecureRequest, IWebhookPersistedOwnerRequest
{
    public Guid AttemptId { get; init; }

    string? ISecureRequest.ResourceId => AttemptId.ToString("D");

    WebhookOwnedResourceKind IWebhookPersistedOwnerRequest.OwnedResourceKind =>
        WebhookOwnedResourceKind.DeliveryAttempt;

    Guid IWebhookPersistedOwnerRequest.OwnedResourceId => AttemptId;
}
