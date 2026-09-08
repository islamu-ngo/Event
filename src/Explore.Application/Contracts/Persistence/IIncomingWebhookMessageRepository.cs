using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public sealed record IncomingWebhookClaimRequest(
    string LeaseOwner,
    int BatchSize,
    DateTime ClaimedAt,
    TimeSpan LeaseDuration);

public sealed record IncomingWebhookClaim(
    Guid IncomingWebhookMessageId,
    Guid TenantId,
    Guid LeaseToken,
    long ProcessingFence,
    int ProcessingGeneration);

public interface IIncomingWebhookMessageRepository
{
    Task<bool> TryCreateAsync(IncomingWebhookMessage message, CancellationToken cancellationToken);

    Task<IncomingWebhookMessage?> GetByProviderMessageIdForUpdateAsync(
        Guid tenantId,
        string provider,
        string providerMessageId,
        CancellationToken cancellationToken);

    Task<IncomingWebhookMessage?> GetByIdempotencyKeyForUpdateAsync(
        Guid tenantId,
        string provider,
        string idempotencyKey,
        CancellationToken cancellationToken) => Task.FromResult<IncomingWebhookMessage?>(null);

    Task<IncomingWebhookMessage?> GetByTenantAndIdForUpdateAsync(
        Guid tenantId,
        Guid incomingWebhookMessageId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<IncomingWebhookClaim>> ClaimDueAsync(
        IncomingWebhookClaimRequest request,
        CancellationToken cancellationToken);

    Task<IncomingWebhookMessage?> GetActiveClaimAsync(
        Guid tenantId,
        Guid incomingWebhookMessageId,
        Guid leaseToken,
        long processingFence,
        int processingGeneration,
        DateTime observedAt,
        CancellationToken cancellationToken);

    Task<bool> RefreshActiveClaimAsync(
        IncomingWebhookMessage message,
        IncomingWebhookClaim claim,
        DateTime observedAt,
        CancellationToken cancellationToken);

    Task<bool> TryRenewClaimAsync(
        Guid tenantId,
        Guid incomingWebhookMessageId,
        Guid leaseToken,
        long processingFence,
        int processingGeneration,
        DateTime observedAt,
        DateTime leaseExpiresAt,
        CancellationToken cancellationToken);

    void TrackAppendedEvidence(IncomingWebhookMessage message);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
