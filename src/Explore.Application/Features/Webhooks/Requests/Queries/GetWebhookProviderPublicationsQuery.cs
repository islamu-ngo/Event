using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Webhooks;

namespace Explore.Application.Features.Webhooks.Requests.Queries;

[AuthorizeResource(ResourceKinds.Webhook, AuthorizationActions.Webhooks.ViewDelivery)]
public sealed record GetWebhookProviderPublicationsQuery : IQuery<IReadOnlyList<WebhookProviderPublicationDto>>, ISecureRequest
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
