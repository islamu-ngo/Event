using Explore.Application.Authorization;
using Explore.Application.DTOs.Webhooks;
using MediatR;

namespace Explore.Application.Features.Webhooks.Requests.Queries;

[AuthorizeResource(ResourceKinds.Webhook, AuthorizationActions.Webhooks.View)]
public sealed record GetWebhookConsumersQuery
    : IRequest<IReadOnlyList<WebhookConsumerDto>>, ISecureRequest, IWebhookOwnerScopedRequest
{
    public int OwnerKindId { get; init; }

    public Guid? OwnerId { get; init; }

    public int Limit { get; init; } = 100;

    string? ISecureRequest.ResourceId => OwnerId?.ToString("D");

}
