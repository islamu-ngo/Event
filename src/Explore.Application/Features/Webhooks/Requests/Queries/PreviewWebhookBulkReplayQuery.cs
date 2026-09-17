using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.Webhooks.Requests.Queries;

[AuthorizeResource(ResourceKinds.Webhook, AuthorizationActions.Webhooks.BulkReplay)]
public sealed record PreviewWebhookBulkReplayQuery : IQuery<WebhookBulkReplayPreviewResult>, ISecureRequest
{
    public Guid TenantId { get; init; }
    public DateTime FromUtc { get; init; }
    public DateTime ToUtc { get; init; }
    public Guid? WebhookConsumerId { get; init; }
    public Guid? WebhookEndpointId { get; init; }
    public string? EventType { get; init; }
    public int MaxItems { get; init; }

    string? ISecureRequest.ResourceId => TenantId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new TenantScopedAuthorizationFacts(TenantId);
}
