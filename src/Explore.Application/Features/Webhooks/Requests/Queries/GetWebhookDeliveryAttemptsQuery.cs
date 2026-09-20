using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Webhooks;

namespace Explore.Application.Features.Webhooks.Requests.Queries;

[AuthorizeResource(ResourceKinds.Webhook, AuthorizationActions.Webhooks.ViewDelivery)]
public sealed record GetWebhookDeliveryAttemptsQuery
    : IQuery<IReadOnlyList<WebhookDeliveryAttemptDto>>, ISecureRequest, IWebhookOwnerScopedRequest
{
    public int OwnerKindId { get; init; }

    public Guid? OwnerId { get; init; }

    public Guid? MessageId { get; init; }

    public Guid? EndpointId { get; init; }

    public int Limit { get; init; } = 100;

    string? ISecureRequest.ResourceId => OwnerId?.ToString("D");

}
