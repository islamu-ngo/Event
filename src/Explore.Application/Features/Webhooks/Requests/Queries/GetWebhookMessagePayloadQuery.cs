using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.Webhooks.Requests.Queries;

[AuthorizeResource(ResourceKinds.Webhook, AuthorizationActions.Webhooks.ViewPayload)]
public sealed record GetWebhookMessagePayloadQuery
    : IQuery<WebhookMessagePayloadReadResult>, ISecureRequest, IWebhookPersistedOwnerRequest
{
    public Guid MessageId { get; init; }

    string? ISecureRequest.ResourceId => MessageId.ToString("D");

    WebhookOwnedResourceKind IWebhookPersistedOwnerRequest.OwnedResourceKind =>
        WebhookOwnedResourceKind.Message;

    Guid IWebhookPersistedOwnerRequest.OwnedResourceId => MessageId;
}
