using Explore.Application.Authorization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Webhooks.Requests.Queries;

[AuthorizeResource(ResourceKinds.Webhook, AuthorizationActions.Webhooks.ViewPayload)]
public sealed record GetWebhookMessagePayloadQuery
    : IRequest<WebhookMessagePayloadReadResult>, ISecureRequest, IWebhookPersistedOwnerRequest
{
    public Guid MessageId { get; init; }

    string? ISecureRequest.ResourceId => MessageId.ToString("D");

    WebhookOwnedResourceKind IWebhookPersistedOwnerRequest.OwnedResourceKind =>
        WebhookOwnedResourceKind.Message;

    Guid IWebhookPersistedOwnerRequest.OwnedResourceId => MessageId;
}
