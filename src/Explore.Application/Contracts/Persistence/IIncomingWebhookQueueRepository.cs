namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Bounded Global authority for incoming queue ownership, claiming and lease renewal.
/// Execution and effect settlement remain in a fresh exact-tenant scope.
/// </summary>
public interface IIncomingWebhookQueueRepository
{
    const int MaximumBatchSize = 1000;

    Task<IReadOnlyList<IncomingWebhookClaim>> ClaimMessagesAsync(
        IncomingWebhookClaimRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<IncomingWebhookEffectClaim>> ClaimEffectsAsync(
        IncomingWebhookEffectClaimRequest request, CancellationToken cancellationToken);

    Task<bool> TryRenewMessageAsync(
        IncomingWebhookClaim claim, DateTime observedAt, DateTime leaseExpiresAt, CancellationToken cancellationToken);

    Task<bool> TryRenewEffectAsync(
        IncomingWebhookEffectClaim claim, DateTime observedAt, DateTime leaseExpiresAt, CancellationToken cancellationToken);
}
