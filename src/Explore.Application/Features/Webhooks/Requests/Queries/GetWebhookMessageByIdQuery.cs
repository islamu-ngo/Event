using Explore.Application.Authorization;
using Explore.Application.DTOs.Webhooks;
using MediatR;

namespace Explore.Application.Features.Webhooks.Requests.Queries;

[AuthorizeResource(ResourceKinds.Webhook, AuthorizationActions.Webhooks.ViewDelivery)]
public sealed record GetWebhookMessageByIdQuery
    : IRequest<WebhookMessageDto?>, ISecureRequest, IWebhookPersistedOwnerRequest
{
    public Guid MessageId { get; init; }

    string? ISecureRequest.ResourceId => MessageId.ToString("D");

    WebhookOwnedResourceKind IWebhookPersistedOwnerRequest.OwnedResourceKind =>
        WebhookOwnedResourceKind.Message;

    Guid IWebhookPersistedOwnerRequest.OwnedResourceId => MessageId;
}
