using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Responses;

namespace Explore.Application.Features.Webhooks.Requests.Commands;

[AuthorizeResource(ResourceKinds.Webhook, AuthorizationActions.Webhooks.Update)]
public sealed record UpdateWebhookEndpointCommand
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest, IWebhookPersistedOwnerRequest
{
    public Guid EndpointId { get; init; }

    public UpdateWebhookEndpointDestinationDto? Destination { get; init; }
    public UpdateWebhookEndpointSubscriptionsDto? Subscriptions { get; init; }
    public UpdateWebhookEndpointDeliveryPolicyDto? DeliveryPolicy { get; init; }
    public required UpdateWebhookEndpointGovernanceDto Governance { get; init; }

    string? ISecureRequest.ResourceId => EndpointId.ToString("D");

    WebhookOwnedResourceKind IWebhookPersistedOwnerRequest.OwnedResourceKind =>
        WebhookOwnedResourceKind.Endpoint;

    Guid IWebhookPersistedOwnerRequest.OwnedResourceId => EndpointId;
}
