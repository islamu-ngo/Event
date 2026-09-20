using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.Webhooks.Requests.Commands;

[AuthorizeResource(ResourceKinds.Webhook, AuthorizationActions.Webhooks.Pause)]
public sealed record PauseWebhookEndpointCommand
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest, IWebhookPersistedOwnerRequest
{
    public Guid EndpointId { get; init; }

    public Guid ActorUserId { get; init; }

    public long ExpectedDeliveryStateVersion { get; init; }

    public string ReasonCode { get; init; } = string.Empty;

    string? ISecureRequest.ResourceId => EndpointId == Guid.Empty ? null : EndpointId.ToString("D");

    WebhookOwnedResourceKind IWebhookPersistedOwnerRequest.OwnedResourceKind =>
        WebhookOwnedResourceKind.Endpoint;

    Guid IWebhookPersistedOwnerRequest.OwnedResourceId => EndpointId;
}
