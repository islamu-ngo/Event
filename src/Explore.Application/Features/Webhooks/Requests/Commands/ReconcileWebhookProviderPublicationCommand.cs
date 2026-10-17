using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.Webhooks.Requests.Commands;

public sealed record ReconcileWebhookProviderPublicationCommand
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest, IWebhookPersistedOwnerRequest
{
    public Guid TenantId { get; init; }
    public Guid PublicationId { get; init; }
    public Guid ActorUserId { get; init; }
    public long ExpectedConcurrencyVersion { get; init; }
    public string ExternalProviderMessageId { get; init; } = string.Empty;
    public string ReasonCode { get; init; } = string.Empty;

    string? ISecureRequest.ResourceId => PublicationId == Guid.Empty ? null : PublicationId.ToString("D");

    WebhookOwnedResourceKind IWebhookPersistedOwnerRequest.OwnedResourceKind =>
        WebhookOwnedResourceKind.ProviderPublication;

    Guid IWebhookPersistedOwnerRequest.OwnedResourceId => PublicationId;
}
