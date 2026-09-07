using Explore.Application.Authorization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Webhooks.Requests.Commands;

[AuthorizeResource(ResourceKinds.Webhook, AuthorizationActions.Webhooks.AbandonPublication)]
public sealed record AbandonWebhookProviderPublicationCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid TenantId { get; init; }
    public Guid PublicationId { get; init; }
    public Guid ActorUserId { get; init; }
    public long ExpectedConcurrencyVersion { get; init; }
    public string ReasonCode { get; init; } = string.Empty;

    string? ISecureRequest.ResourceId => PublicationId == Guid.Empty ? null : PublicationId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        PublicationId == Guid.Empty
        ? null
        : new TenantScopedAuthorizationFacts(TenantId);
}
