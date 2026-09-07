using Explore.Application.Authorization;
using Explore.Application.DTOs.Webhooks;
using MediatR;

namespace Explore.Application.Features.Webhooks.Requests.Queries;

[AuthorizeResource(ResourceKinds.Webhook, AuthorizationActions.Webhooks.BulkReplay)]
public sealed record GetWebhookBulkReplayOperationQuery : IRequest<WebhookBulkReplayOperationDto?>, ISecureRequest
{
    public Guid TenantId { get; init; }
    public Guid OperationId { get; init; }

    string? ISecureRequest.ResourceId => OperationId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new TenantScopedAuthorizationFacts(TenantId);
}
