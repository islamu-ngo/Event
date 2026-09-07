using Explore.Application.Authorization;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Webhooks.Requests.Queries;

[AuthorizeResource(ResourceKinds.Webhook, AuthorizationActions.Webhooks.ViewDelivery)]
public sealed record GetIncomingWebhookEffectStatusQuery
    : IRequest<BaseCommandResponse<IReadOnlyList<IncomingWebhookEffectStatusDto>>>, ISecureRequest
{
    public Guid TenantId { get; init; }
    public int Limit { get; init; } = 50;

    string? ISecureRequest.ResourceId => TenantId == Guid.Empty ? null : TenantId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        TenantId == Guid.Empty
        ? null
        : new TenantScopedAuthorizationFacts(TenantId);
}
