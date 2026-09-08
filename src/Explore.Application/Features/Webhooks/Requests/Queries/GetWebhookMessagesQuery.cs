using Explore.Application.Authorization;
using Explore.Application.DTOs.Webhooks;
using MediatR;

namespace Explore.Application.Features.Webhooks.Requests.Queries;

[AuthorizeResource(ResourceKinds.Webhook, AuthorizationActions.Webhooks.ViewDelivery)]
public sealed record GetWebhookMessagesQuery
    : IRequest<IReadOnlyList<WebhookMessageDto>>, ISecureRequest, IWebhookOwnerScopedRequest
{
    public int OwnerKindId { get; init; }

    public Guid? OwnerId { get; init; }

    public int Limit { get; init; } = 100;

    string? ISecureRequest.ResourceId => OwnerId?.ToString("D");

}
