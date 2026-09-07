using Explore.Application.Authorization;
using Explore.Application.DTOs.Webhooks;
using MediatR;

namespace Explore.Application.Features.Webhooks.Requests.Queries;

[AuthorizeResource(ResourceKinds.Webhook, AuthorizationActions.Webhooks.View)]
public sealed record GetWebhookEndpointByIdQuery
    : IRequest<WebhookEndpointDto?>, ISecureRequest, IWebhookPersistedOwnerRequest
{
    public Guid EndpointId { get; init; }

    string? ISecureRequest.ResourceId => EndpointId.ToString("D");

    WebhookOwnedResourceKind IWebhookPersistedOwnerRequest.OwnedResourceKind =>
        WebhookOwnedResourceKind.Endpoint;

    Guid IWebhookPersistedOwnerRequest.OwnedResourceId => EndpointId;
}
