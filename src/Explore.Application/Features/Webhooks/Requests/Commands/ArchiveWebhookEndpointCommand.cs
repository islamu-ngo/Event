using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.Webhooks.Requests.Commands;

[AuthorizeResource(ResourceKinds.Webhook, AuthorizationActions.Webhooks.Delete)]
public sealed record ArchiveWebhookEndpointCommand
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest, IWebhookPersistedOwnerRequest
{
    public Guid EndpointId { get; init; }

    string? ISecureRequest.ResourceId => EndpointId.ToString("D");

    WebhookOwnedResourceKind IWebhookPersistedOwnerRequest.OwnedResourceKind =>
        WebhookOwnedResourceKind.Endpoint;

    Guid IWebhookPersistedOwnerRequest.OwnedResourceId => EndpointId;
}
