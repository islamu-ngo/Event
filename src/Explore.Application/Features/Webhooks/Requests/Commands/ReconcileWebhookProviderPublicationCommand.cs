using Explore.Application.Authorization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Webhooks.Requests.Commands;

[AuthorizeResource(ResourceKinds.Webhook, AuthorizationActions.Webhooks.ReconcilePublication)]
public sealed record ReconcileWebhookProviderPublicationCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid TenantId { get; init; }
    public Guid PublicationId { get; init; }
    public Guid ActorUserId { get; init; }
    public long ExpectedConcurrencyVersion { get; init; }
    public string ExternalProviderMessageId { get; init; } = string.Empty;
    public string ReasonCode { get; init; } = string.Empty;

    string? ISecureRequest.ResourceId => PublicationId == Guid.Empty ? null : PublicationId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        PublicationId == Guid.Empty
        ? null
        : new TenantScopedAuthorizationFacts(TenantId);
}
