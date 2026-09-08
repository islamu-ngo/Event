using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Explore.Persistence.Repositories;

public sealed class IncomingWebhookEffectOutboxRepository(ExploreDbContext dbContext)
    : IIncomingWebhookEffectOutboxRepository
{
    public Task<IncomingWebhookEffectOutbox?> GetByProviderIdentityAsync(
        Guid tenantId,
        string provider,
        string providerDecisionId,
        string effectKind,
        CancellationToken cancellationToken)
    {
        var normalizedProvider = provider.Trim().ToLowerInvariant();
        var normalizedProviderDecisionId = providerDecisionId.Trim();
        var normalizedEffectKind = IncomingWebhookEffectReceipt.NormalizeEffectKind(effectKind);

        return dbContext.IncomingWebhookEffectOutboxes
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .FirstOrDefaultAsync(pointer =>
                pointer.TenantId == tenantId &&
                pointer.Provider == normalizedProvider &&
                pointer.ProviderDecisionId == normalizedProviderDecisionId &&
                pointer.EffectKind == normalizedEffectKind,
                cancellationToken);
    }

    public async Task<IReadOnlyList<IncomingWebhookEffectClaim>> ClaimDueAsync(
        IncomingWebhookEffectClaimRequest request,
        CancellationToken cancellationToken)
    {
        if (request.BatchSize is < 1 or > 1000 ||
            request.LeaseDuration <= TimeSpan.Zero ||
            request.ClaimedAt.Kind != DateTimeKind.Utc ||
            string.IsNullOrWhiteSpace(request.LeaseOwner))
        {
            return [];
        }

        var leaseOwner = request.LeaseOwner.Trim();
        if (leaseOwner.Length > IncomingWebhookEffectOutbox.MaxLeaseOwnerLength)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            _ = await RelationalNamedLock.AcquireTransactionAsync(
                dbContext,
                "incoming-webhook-effect-claim",
                cancellationToken);

            var candidates = await dbContext.IncomingWebhookEffectOutboxes
                .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookWorkerCrossTenantQueue)
                .Where(pointer =>
                    ((pointer.Status == OutboxMessageStatus.Pending ||
                      pointer.Status == OutboxMessageStatus.Failed) &&
                     (pointer.NextAttemptAt == null || pointer.NextAttemptAt <= request.ClaimedAt)) ||
                    (pointer.Status == OutboxMessageStatus.Processing &&
                     pointer.ProcessingLeaseExpiresAt != null &&
                     pointer.ProcessingLeaseExpiresAt <= request.ClaimedAt))
                .OrderBy(pointer => pointer.NextAttemptAt ?? pointer.CreatedAt)
                .ThenBy(pointer => pointer.CreatedAt)
                .ThenBy(pointer => pointer.Id)
                .Take(request.BatchSize)
                .ToListAsync(cancellationToken);

            var leaseExpiresAt = request.ClaimedAt.Add(request.LeaseDuration);
            var claims = new List<IncomingWebhookEffectClaim>(candidates.Count);
            foreach (var pointer in candidates)
            {
                if (pointer.Status == OutboxMessageStatus.Processing)
                {
                    pointer.RecoverExpiredClaim(request.ClaimedAt);
                }

                var leaseToken = Guid.CreateVersion7();
                pointer.Claim(leaseOwner, leaseToken, leaseExpiresAt, request.ClaimedAt);
                claims.Add(new IncomingWebhookEffectClaim(
                    pointer.Id,
                    pointer.TenantId,
                    leaseToken,
                    pointer.ProcessingFence,
                    pointer.ProcessingGeneration));
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return claims;
        });
    }

    public Task<IncomingWebhookEffectOutbox?> GetActiveClaimAsync(
        IncomingWebhookEffectClaim claim,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        return dbContext.IncomingWebhookEffectOutboxes
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .Include(pointer => pointer.IncomingWebhookMessage)
            .AsNoTracking()
            .FirstOrDefaultAsync(pointer =>
                pointer.TenantId == claim.TenantId &&
                pointer.Id == claim.EffectOutboxId &&
                pointer.Status == OutboxMessageStatus.Processing &&
                pointer.ProcessingLeaseToken == claim.LeaseToken &&
                pointer.ProcessingFence == claim.ProcessingFence &&
                pointer.ProcessingGeneration == claim.ProcessingGeneration &&
                pointer.ProcessingLeaseExpiresAt > observedAt,
                cancellationToken);
    }

    public Task<IncomingWebhookEffectOutbox?> GetByTenantAndIdForUpdateAsync(
        Guid tenantId,
        Guid effectOutboxId,
        CancellationToken cancellationToken)
    {
        return dbContext.IncomingWebhookEffectOutboxes
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .Include(pointer => pointer.IncomingWebhookMessage)
            .FirstOrDefaultAsync(pointer =>
                pointer.TenantId == tenantId && pointer.Id == effectOutboxId,
                cancellationToken);
    }

    public Task<IncomingWebhookEffectOutbox?> GetByTenantAndIdAsync(
        Guid tenantId,
        Guid effectOutboxId,
        CancellationToken cancellationToken)
    {
        return dbContext.IncomingWebhookEffectOutboxes
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .FirstOrDefaultAsync(pointer =>
                pointer.TenantId == tenantId && pointer.Id == effectOutboxId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<IncomingWebhookEffectOutbox>> GetStatusRowsAsync(
        Guid tenantId,
        int limit,
        CancellationToken cancellationToken)
    {
        return await dbContext.IncomingWebhookEffectOutboxes
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .Where(pointer => pointer.TenantId == tenantId)
            .OrderByDescending(pointer => pointer.CreatedAt)
            .ThenByDescending(pointer => pointer.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryRenewClaimAsync(
        IncomingWebhookEffectClaim claim,
        DateTime observedAt,
        DateTime leaseExpiresAt,
        CancellationToken cancellationToken)
    {
        if (observedAt.Kind != DateTimeKind.Utc ||
            leaseExpiresAt.Kind != DateTimeKind.Utc ||
            leaseExpiresAt <= observedAt)
        {
            return false;
        }

        var affected = await dbContext.IncomingWebhookEffectOutboxes
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .Where(pointer =>
                pointer.TenantId == claim.TenantId &&
                pointer.Id == claim.EffectOutboxId &&
                pointer.Status == OutboxMessageStatus.Processing &&
                pointer.ProcessingLeaseToken == claim.LeaseToken &&
                pointer.ProcessingFence == claim.ProcessingFence &&
                pointer.ProcessingGeneration == claim.ProcessingGeneration &&
                pointer.ProcessingLeaseExpiresAt > observedAt &&
                pointer.ProcessingLeaseExpiresAt < leaseExpiresAt)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(pointer => pointer.ProcessingLeaseExpiresAt, leaseExpiresAt)
                .SetProperty(pointer => pointer.UpdatedAt, observedAt), cancellationToken);
        return affected == 1;
    }

    public Task<int> CountDueAsync(DateTime observedAt, CancellationToken cancellationToken) =>
        dbContext.IncomingWebhookEffectOutboxes
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookWorkerCrossTenantQueue)
            .CountAsync(pointer =>
                (pointer.Status == OutboxMessageStatus.Pending ||
                 pointer.Status == OutboxMessageStatus.Failed) &&
                (pointer.NextAttemptAt == null || pointer.NextAttemptAt <= observedAt),
                cancellationToken);

    public Task<int> CountStaleAsync(DateTime observedAt, CancellationToken cancellationToken) =>
        dbContext.IncomingWebhookEffectOutboxes
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookWorkerCrossTenantQueue)
            .CountAsync(pointer =>
                pointer.Status == OutboxMessageStatus.Processing &&
                pointer.ProcessingLeaseExpiresAt != null &&
                pointer.ProcessingLeaseExpiresAt <= observedAt,
                cancellationToken);

    public async Task AddAsync(
        IncomingWebhookEffectOutbox pointer,
        CancellationToken cancellationToken)
    {
        await dbContext.IncomingWebhookEffectOutboxes.AddAsync(pointer, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsEffectReceiptIdentityViolation(exception))
        {
            throw new IncomingWebhookEffectReceiptConflictException(exception);
        }
    }

    private bool IsEffectReceiptIdentityViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: { } constraintName
        } &&
        constraintName == RelationalConstraintDescriptorResolver.UniqueIndex<IncomingWebhookEffectReceipt>(
            dbContext,
            nameof(IncomingWebhookEffectReceipt.TenantId),
            nameof(IncomingWebhookEffectReceipt.IncomingWebhookMessageId),
            nameof(IncomingWebhookEffectReceipt.EffectKind)).Name;
}
