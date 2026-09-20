using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Webhooks;

namespace Explore.Application.Features.Webhooks.Requests.Queries;

[AuthorizeResource(ResourceKinds.Webhook, AuthorizationActions.Webhooks.View)]
public sealed record GetWebhookConsumersQuery
    : IQuery<IReadOnlyList<WebhookConsumerDto>>, ISecureRequest, IWebhookOwnerScopedRequest
{
    public int OwnerKindId { get; init; }

    public Guid? OwnerId { get; init; }

    public int Limit { get; init; } = 100;

    string? ISecureRequest.ResourceId => OwnerId?.ToString("D");

}
