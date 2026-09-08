using Explore.Application.Authorization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Webhooks.Requests.Commands;

[AuthorizeResource(ResourceKinds.Webhook, AuthorizationActions.Webhooks.RedriveIncoming)]
public sealed record RedriveIncomingWebhookEffectCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid TenantId { get; init; }
    public Guid EffectOutboxId { get; init; }
    public int ExpectedProcessingGeneration { get; init; }
    public string Reason { get; init; } = string.Empty;

    string? ISecureRequest.ResourceId => EffectOutboxId == Guid.Empty
        ? null
        : EffectOutboxId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        TenantId == Guid.Empty
        ? null
        : new TenantScopedAuthorizationFacts(TenantId);
}
