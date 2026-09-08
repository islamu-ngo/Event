using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public sealed record IncomingWebhookEffectClaimRequest(
    string LeaseOwner,
    int BatchSize,
    DateTime ClaimedAt,
    TimeSpan LeaseDuration);

public sealed record IncomingWebhookEffectClaim(
    Guid EffectOutboxId,
    Guid TenantId,
    Guid LeaseToken,
    long ProcessingFence,
    int ProcessingGeneration);

public interface IIncomingWebhookEffectOutboxRepository
{
    Task<IncomingWebhookEffectOutbox?> GetByProviderIdentityAsync(
        Guid tenantId,
        string provider,
        string providerDecisionId,
        string effectKind,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<IncomingWebhookEffectClaim>> ClaimDueAsync(
        IncomingWebhookEffectClaimRequest request,
        CancellationToken cancellationToken);

    Task<IncomingWebhookEffectOutbox?> GetActiveClaimAsync(
        IncomingWebhookEffectClaim claim,
        DateTime observedAt,
        CancellationToken cancellationToken);

    Task<IncomingWebhookEffectOutbox?> GetByTenantAndIdForUpdateAsync(
        Guid tenantId,
        Guid effectOutboxId,
        CancellationToken cancellationToken);

    Task<IncomingWebhookEffectOutbox?> GetByTenantAndIdAsync(
        Guid tenantId,
        Guid effectOutboxId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<IncomingWebhookEffectOutbox>> GetStatusRowsAsync(
        Guid tenantId,
        int limit,
        CancellationToken cancellationToken);

    Task<bool> TryRenewClaimAsync(
        IncomingWebhookEffectClaim claim,
        DateTime observedAt,
        DateTime leaseExpiresAt,
        CancellationToken cancellationToken);

    Task<int> CountDueAsync(DateTime observedAt, CancellationToken cancellationToken);

    Task<int> CountStaleAsync(DateTime observedAt, CancellationToken cancellationToken);

    Task AddAsync(IncomingWebhookEffectOutbox pointer, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
