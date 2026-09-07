using Explore.Application.Authorization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Webhooks.Requests.Commands;

[AuthorizeResource(ResourceKinds.Webhook, AuthorizationActions.Webhooks.Create)]
public sealed record CreateWebhookEndpointCommand
    : IRequest<BaseCommandResponse<Guid>>, ISecureRequest, IWebhookPersistedOwnerRequest
{
    public Guid ConsumerId { get; init; }

    public required string Url { get; init; }

    public string? Description { get; init; }

    public required string SecretRef { get; init; }

    private IReadOnlyList<Guid> _eventTypeIds = Array.AsReadOnly(Array.Empty<Guid>());

    public IReadOnlyList<Guid> EventTypeIds
    {
        get => _eventTypeIds;
        init => _eventTypeIds = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }

    public int? MaxAttempts { get; init; }

    public int? TimeoutSeconds { get; init; }

    public int? RateLimitPerMinute { get; init; }

    string? ISecureRequest.ResourceId => ConsumerId.ToString("D");

    WebhookOwnedResourceKind IWebhookPersistedOwnerRequest.OwnedResourceKind =>
        WebhookOwnedResourceKind.Consumer;

    Guid IWebhookPersistedOwnerRequest.OwnedResourceId => ConsumerId;
}
