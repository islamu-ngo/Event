using Explore.Application.Authorization;
using Explore.Application.DTOs.Webhooks;
using MediatR;

namespace Explore.Application.Features.Webhooks.Requests.Queries;

[AuthorizeResource(ResourceKinds.Webhook, AuthorizationActions.Webhooks.BulkReplay)]
public sealed record GetWebhookBulkReplayOperationsQuery : IRequest<IReadOnlyList<WebhookBulkReplayOperationDto>>, ISecureRequest
{
    public Guid TenantId { get; init; }
    public int Limit { get; init; } = 100;

    string? ISecureRequest.ResourceId => TenantId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new TenantScopedAuthorizationFacts(TenantId);
}
