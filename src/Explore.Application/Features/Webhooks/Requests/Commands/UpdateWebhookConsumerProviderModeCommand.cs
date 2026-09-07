using Explore.Application.Authorization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Webhooks.Requests.Commands;

[AuthorizeResource(ResourceKinds.Webhook, AuthorizationActions.Webhooks.Update)]
public sealed record UpdateWebhookConsumerProviderModeCommand
    : IRequest<BaseCommandResponse<Guid>>, ISecureRequest, IWebhookPersistedOwnerRequest
{
    public Guid ConsumerId { get; init; }

    public int ProviderModeId { get; init; }

    public int ExpectedConfigurationVersion { get; init; }

    public int PendingWorkDecisionId { get; init; }

    public required string PendingWorkReason { get; init; }

    public bool AcknowledgeUncertainProviderPublications { get; init; }

    string? ISecureRequest.ResourceId => ConsumerId.ToString("D");

    WebhookOwnedResourceKind IWebhookPersistedOwnerRequest.OwnedResourceKind =>
        WebhookOwnedResourceKind.Consumer;

    Guid IWebhookPersistedOwnerRequest.OwnedResourceId => ConsumerId;
}
