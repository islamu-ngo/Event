using Explore.Application.Authorization;
using Explore.Application.DTOs.Webhooks;
using MediatR;

namespace Explore.Application.Features.Webhooks.Requests.Queries;

[AuthorizeResource(ResourceKinds.Webhook, AuthorizationActions.Webhooks.ViewDelivery)]
public sealed record GetWebhookProviderPublicationsQuery : IRequest<IReadOnlyList<WebhookProviderPublicationDto>>, ISecureRequest
{
    public Guid TenantId { get; init; }
    public Guid? WebhookMessageId { get; init; }
    public Guid? WebhookConsumerId { get; init; }
    public int? StatusId { get; init; }
    public int Limit { get; init; } = 100;

    string? ISecureRequest.ResourceId => TenantId == Guid.Empty ? null : TenantId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        TenantId == Guid.Empty
        ? null
        : new TenantScopedAuthorizationFacts(TenantId);
}
