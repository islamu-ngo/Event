using Explore.Application.Authorization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Webhooks.Requests.Commands;

[AuthorizeResource(ResourceKinds.Webhook, AuthorizationActions.Webhooks.BulkReplay)]
public sealed record ScheduleWebhookBulkReplayCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid TenantId { get; init; }
    public Guid ActorUserId { get; init; }
    public Guid OperationKey { get; init; }
    public DateTime FromUtc { get; init; }
    public DateTime ToUtc { get; init; }
    public Guid? WebhookConsumerId { get; init; }
    public Guid? WebhookEndpointId { get; init; }
    public string? EventType { get; init; }
    public int MaxItems { get; init; }
    public required string ReasonCode { get; init; }

    string? ISecureRequest.ResourceId => TenantId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new TenantScopedAuthorizationFacts(TenantId);
}
