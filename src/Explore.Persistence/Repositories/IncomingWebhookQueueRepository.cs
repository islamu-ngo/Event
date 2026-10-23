using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;

namespace Explore.Persistence.Repositories;

/// <summary>
/// Acquires only explicit Global leases. PostgreSQL exposes claim identities through
/// execute-only functions, never private messages, receipts or effect envelopes.
/// </summary>
public sealed class IncomingWebhookQueueRepository(IGlobalExploreDbContextFactory globalFactory)
    : IIncomingWebhookQueueRepository
{
    public async Task<IReadOnlyList<IncomingWebhookClaim>> ClaimMessagesAsync(
        IncomingWebhookClaimRequest request, CancellationToken cancellationToken)
    {
        if (request.BatchSize is < 1 or > IIncomingWebhookQueueRepository.MaximumBatchSize ||
            request.LeaseDuration <= TimeSpan.Zero)
            return [];
        if (request.ClaimedAt.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Claim time must use UTC kind.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.LeaseOwner))
            throw new ArgumentException("Lease owner is required.", nameof(request));
        string owner = request.LeaseOwner.Trim();
        if (owner.Length > IncomingWebhookMessage.MaxLeaseOwnerLength)
            throw new ArgumentOutOfRangeException(nameof(request));

        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        return await IncomingWebhookNativeQueue.TryClaimMessagesAsync(context, request, owner, cancellationToken)
            ?? await new IncomingWebhookMessageRepository(context).ClaimDueAsync(request, cancellationToken);
    }

    public async Task<IReadOnlyList<IncomingWebhookEffectClaim>> ClaimEffectsAsync(
        IncomingWebhookEffectClaimRequest request, CancellationToken cancellationToken)
    {
        if (request.BatchSize is < 1 or > IIncomingWebhookQueueRepository.MaximumBatchSize ||
            request.LeaseDuration <= TimeSpan.Zero || request.ClaimedAt.Kind != DateTimeKind.Utc ||
            string.IsNullOrWhiteSpace(request.LeaseOwner))
            return [];
        string owner = request.LeaseOwner.Trim();
        if (owner.Length > IncomingWebhookEffectOutbox.MaxLeaseOwnerLength)
            throw new ArgumentOutOfRangeException(nameof(request));

        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        return await IncomingWebhookNativeQueue.TryClaimEffectsAsync(context, request, owner, cancellationToken)
            ?? await new IncomingWebhookEffectOutboxRepository(context).ClaimDueAsync(request, cancellationToken);
    }

    public async Task<bool> TryRenewMessageAsync(
        IncomingWebhookClaim claim, DateTime observedAt, DateTime leaseExpiresAt, CancellationToken cancellationToken)
    {
        if (!ValidRenewal(observedAt, leaseExpiresAt))
            return false;
        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        return await IncomingWebhookNativeQueue.TryRenewMessageAsync(context, claim, observedAt, leaseExpiresAt, cancellationToken)
            ?? await new IncomingWebhookMessageRepository(context).TryRenewClaimAsync(
                claim.TenantId, claim.IncomingWebhookMessageId, claim.LeaseToken,
                claim.ProcessingFence, claim.ProcessingGeneration, observedAt, leaseExpiresAt, cancellationToken);
    }

    public async Task<bool> TryRenewEffectAsync(
        IncomingWebhookEffectClaim claim, DateTime observedAt, DateTime leaseExpiresAt, CancellationToken cancellationToken)
    {
        if (!ValidRenewal(observedAt, leaseExpiresAt))
            return false;
        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        return await IncomingWebhookNativeQueue.TryRenewEffectAsync(context, claim, observedAt, leaseExpiresAt, cancellationToken)
            ?? await new IncomingWebhookEffectOutboxRepository(context).TryRenewClaimAsync(
                claim, observedAt, leaseExpiresAt, cancellationToken);
    }

    private static bool ValidRenewal(DateTime observedAt, DateTime leaseExpiresAt) =>
        observedAt.Kind == DateTimeKind.Utc && leaseExpiresAt.Kind == DateTimeKind.Utc && leaseExpiresAt > observedAt;
}
