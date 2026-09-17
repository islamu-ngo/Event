using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.Webhooks.Requests.Commands;

[AuthorizeResource(ResourceKinds.Webhook, AuthorizationActions.Webhooks.RedriveIncoming)]
public sealed record RedriveIncomingWebhookCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid TenantId { get; init; }

    public Guid IncomingWebhookMessageId { get; init; }

    public int ExpectedProcessingGeneration { get; init; }

    public string Reason { get; init; } = string.Empty;

    string? ISecureRequest.ResourceId => IncomingWebhookMessageId == Guid.Empty
        ? null
        : IncomingWebhookMessageId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        TenantId == Guid.Empty
        ? null
        : new TenantScopedAuthorizationFacts(TenantId);
}
