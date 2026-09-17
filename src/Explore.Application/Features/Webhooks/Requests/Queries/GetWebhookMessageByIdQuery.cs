using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Webhooks;

namespace Explore.Application.Features.Webhooks.Requests.Queries;

[AuthorizeResource(ResourceKinds.Webhook, AuthorizationActions.Webhooks.ViewDelivery)]
public sealed record GetWebhookMessageByIdQuery
    : IQuery<WebhookMessageDto?>, ISecureRequest, IWebhookPersistedOwnerRequest
{
    public Guid MessageId { get; init; }

    string? ISecureRequest.ResourceId => MessageId.ToString("D");

    WebhookOwnedResourceKind IWebhookPersistedOwnerRequest.OwnedResourceKind =>
        WebhookOwnedResourceKind.Message;

    Guid IWebhookPersistedOwnerRequest.OwnedResourceId => MessageId;
}
