using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.Webhooks.Requests.Commands;

[AuthorizeResource(ResourceKinds.Webhook, AuthorizationActions.Webhooks.ManageProvider)]
public sealed record RepairWebhookProviderBindingCommand
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest, IWebhookPersistedOwnerRequest
{
    public Guid ConsumerId { get; init; }

    public string ExternalApplicationId { get; init; } = string.Empty;

    public string ReasonCode { get; init; } = string.Empty;

    string? ISecureRequest.ResourceId => ConsumerId == Guid.Empty
        ? null
        : ConsumerId.ToString("D");

    WebhookOwnedResourceKind IWebhookPersistedOwnerRequest.OwnedResourceKind =>
        WebhookOwnedResourceKind.Consumer;

    Guid IWebhookPersistedOwnerRequest.OwnedResourceId => ConsumerId;
}
