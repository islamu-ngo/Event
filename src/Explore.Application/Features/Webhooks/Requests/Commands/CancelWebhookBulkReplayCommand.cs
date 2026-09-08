using Explore.Application.Authorization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Webhooks.Requests.Commands;

[AuthorizeResource(ResourceKinds.Webhook, AuthorizationActions.Webhooks.BulkReplay)]
public sealed record CancelWebhookBulkReplayCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid TenantId { get; init; }
    public Guid ActorUserId { get; init; }
    public Guid OperationId { get; init; }
    public long ExpectedConcurrencyVersion { get; init; }
    public required string ReasonCode { get; init; }

    string? ISecureRequest.ResourceId => OperationId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new TenantScopedAuthorizationFacts(TenantId);
}
