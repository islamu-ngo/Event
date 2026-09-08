using Explore.Application.Authorization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Webhooks.Requests.Commands;

[AuthorizeResource(ResourceKinds.Webhook, AuthorizationActions.Webhooks.Retry)]
public sealed record RetryWebhookDeliveryAttemptCommand
    : IRequest<BaseCommandResponse<Guid>>, ISecureRequest, IWebhookPersistedOwnerRequest
{
    public Guid AttemptId { get; init; }

    string? ISecureRequest.ResourceId => AttemptId.ToString("D");

    WebhookOwnedResourceKind IWebhookPersistedOwnerRequest.OwnedResourceKind =>
        WebhookOwnedResourceKind.DeliveryAttempt;

    Guid IWebhookPersistedOwnerRequest.OwnedResourceId => AttemptId;
}
