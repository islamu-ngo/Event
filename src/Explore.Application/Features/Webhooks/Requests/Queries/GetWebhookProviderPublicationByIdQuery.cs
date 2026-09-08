using Explore.Application.Authorization;
using Explore.Application.DTOs.Webhooks;
using MediatR;

namespace Explore.Application.Features.Webhooks.Requests.Queries;

[AuthorizeResource(ResourceKinds.Webhook, AuthorizationActions.Webhooks.ViewDelivery)]
public sealed record GetWebhookProviderPublicationByIdQuery : IRequest<WebhookProviderPublicationDto?>, ISecureRequest
{
    public Guid TenantId { get; init; }
    public Guid PublicationId { get; init; }

    string? ISecureRequest.ResourceId => PublicationId == Guid.Empty ? null : PublicationId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        PublicationId == Guid.Empty
        ? null
        : new TenantScopedAuthorizationFacts(TenantId);
}
