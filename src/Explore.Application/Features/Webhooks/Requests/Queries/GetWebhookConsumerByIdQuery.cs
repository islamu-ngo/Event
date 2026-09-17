using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Webhooks;

namespace Explore.Application.Features.Webhooks.Requests.Queries;

[AuthorizeResource(ResourceKinds.Webhook, AuthorizationActions.Webhooks.View)]
public sealed record GetWebhookConsumerByIdQuery
    : IQuery<WebhookConsumerDto?>, ISecureRequest, IWebhookPersistedOwnerRequest
{
    public Guid ConsumerId { get; init; }

    string? ISecureRequest.ResourceId => ConsumerId.ToString("D");

    WebhookOwnedResourceKind IWebhookPersistedOwnerRequest.OwnedResourceKind =>
        WebhookOwnedResourceKind.Consumer;

    Guid IWebhookPersistedOwnerRequest.OwnedResourceId => ConsumerId;
}
