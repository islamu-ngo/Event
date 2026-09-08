using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class IntegrationSyncOutboxRepository(ExploreDbContext dbContext) : IIntegrationSyncOutboxRepository
{
    public async Task<IntegrationSyncOutbox> Create(IntegrationSyncOutbox outbox, CancellationToken cancellationToken)
    {
        await dbContext.IntegrationSyncOutbox.AddAsync(outbox, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return outbox;
    }

    public async Task<IReadOnlyList<IntegrationSyncOutbox>> GetPendingBatch(
        int batchSize,
        DateTime now,
        DateTime staleProcessingStartedBefore,
        CancellationToken cancellationToken)
    {
        return await dbContext.IntegrationSyncOutbox
            .IgnoreTenantFilter(TenantFilterBypassReasons.IntegrationSyncWorkerCrossTenantQueue)
            .AsNoTracking()
            .Where(o =>
                ((o.Status == IntegrationSyncStatus.Pending || o.Status == IntegrationSyncStatus.RetryScheduled) &&
                 (o.NextAttemptAt == null || o.NextAttemptAt <= now)) ||
                (o.Status == IntegrationSyncStatus.Processing &&
                 (o.ProcessingStartedAt == null || o.ProcessingStartedAt <= staleProcessingStartedBefore)))
            .OrderBy(o => o.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryClaimAsync(
        IntegrationSyncClaimRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await dbContext.IntegrationSyncOutbox
            .IgnoreTenantFilter(TenantFilterBypassReasons.IntegrationSyncWorkerCrossTenantQueue)
            .Where(o => o.TenantId == request.TenantId && o.Id == request.OutboxId &&
                (((o.Status == IntegrationSyncStatus.Pending || o.Status == IntegrationSyncStatus.RetryScheduled) &&
                  (o.NextAttemptAt == null || o.NextAttemptAt <= request.StartedAt)) ||
                 (o.Status == IntegrationSyncStatus.Processing &&
                  o.ProcessingStartedAt <= request.StaleProcessingStartedBefore &&
                  o.LastError != IntegrationSyncFailureCodes.ProviderHandoffInDoubt)))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(o => o.Status, IntegrationSyncStatus.Processing)
                .SetProperty(o => o.ProcessingStartedAt, request.StartedAt)
                .SetProperty(o => o.ProcessingLeaseToken, request.LeaseToken)
                .SetProperty(o => o.AttemptCount, o => o.AttemptCount + 1)
                .SetProperty(o => o.LastError, (string?)null)
                .SetProperty(o => o.UpdatedAt, request.StartedAt),
                cancellationToken);

        return updated > 0;
    }

    public Task<IntegrationSyncOutbox?> GetActiveClaimAsync(
        IntegrationSyncClaimIdentity claim,
        CancellationToken cancellationToken)
    {
        return dbContext.IntegrationSyncOutbox
            .IgnoreTenantFilter(TenantFilterBypassReasons.IntegrationSyncWorkerCrossTenantQueue)
            .AsNoTracking()
            .FirstOrDefaultAsync(outbox =>
                outbox.TenantId == claim.TenantId &&
                outbox.Id == claim.OutboxId &&
                outbox.Status == IntegrationSyncStatus.Processing &&
                outbox.ProcessingLeaseToken == claim.LeaseToken &&
                outbox.ProcessingStartedAt == claim.ProcessingStartedAt,
                cancellationToken);
    }

    public async Task<bool> MarkProviderHandoffStartedAsync(
        IntegrationSyncClaimIdentity claim,
        DateTime startedAt,
        CancellationToken cancellationToken)
    {
        int updated = await ActiveClaim(claim).ExecuteUpdateAsync(setters => setters
            .SetProperty(o => o.LastError, IntegrationSyncFailureCodes.ProviderHandoffInDoubt)
            .SetProperty(o => o.UpdatedAt, startedAt), cancellationToken);
        return updated == 1;
    }

    public async Task<bool> CompleteAsync(
        IntegrationSyncClaimIdentity claim,
        DateTime completedAt,
        CancellationToken cancellationToken)
    {
        int updated = await ActiveClaim(claim).ExecuteUpdateAsync(setters => setters
                .SetProperty(o => o.Status, IntegrationSyncStatus.Completed)
                .SetProperty(o => o.CompletedAt, completedAt)
                .SetProperty(o => o.ProcessingStartedAt, (DateTime?)null)
                .SetProperty(o => o.ProcessingLeaseToken, (Guid?)null)
                .SetProperty(o => o.LastError, (string?)null)
                .SetProperty(o => o.UpdatedAt, completedAt),
                cancellationToken);
        return updated == 1;
    }

    public async Task<bool> FailAsync(
        IntegrationSyncClaimIdentity claim,
        string errorMessage,
        bool isRetryable,
        TimeSpan retryDelay,
        int maxAttempts,
        DateTime failedAt,
        CancellationToken cancellationToken)
    {
        var entry = await ActiveClaim(claim).AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (entry is null)
        {
            return false;
        }

        var exhausted = !isRetryable || entry.AttemptCount >= Math.Min(entry.MaxAttempts, maxAttempts);
        string boundedError = errorMessage.Length > 2000 ? errorMessage[..2000] : errorMessage;
        int updated = await ActiveClaim(claim).ExecuteUpdateAsync(setters => setters
            .SetProperty(o => o.Status, exhausted ? IntegrationSyncStatus.DeadLettered : IntegrationSyncStatus.RetryScheduled)
            .SetProperty(o => o.LastFailureAt, failedAt)
            .SetProperty(o => o.LastError, boundedError)
            .SetProperty(o => o.NextAttemptAt, exhausted ? null : failedAt.Add(retryDelay))
            .SetProperty(o => o.DeadLetteredAt, exhausted ? failedAt : null)
            .SetProperty(o => o.ProcessingStartedAt, (DateTime?)null)
            .SetProperty(o => o.ProcessingLeaseToken, (Guid?)null)
            .SetProperty(o => o.UpdatedAt, failedAt), cancellationToken);
        return updated == 1;
    }

    public async Task<bool> ParkAmbiguousAsync(
        IntegrationSyncClaimIdentity claim,
        DateTime parkedAt,
        CancellationToken cancellationToken)
    {
        int updated = await ActiveClaim(claim).ExecuteUpdateAsync(setters => setters
            .SetProperty(o => o.Status, IntegrationSyncStatus.DeadLettered)
            .SetProperty(o => o.LastFailureAt, parkedAt)
            .SetProperty(o => o.LastError, IntegrationSyncFailureCodes.ProviderOutcomeAmbiguous)
            .SetProperty(o => o.NextAttemptAt, (DateTime?)null)
            .SetProperty(o => o.DeadLetteredAt, parkedAt)
            .SetProperty(o => o.ProcessingStartedAt, (DateTime?)null)
            .SetProperty(o => o.ProcessingLeaseToken, (Guid?)null)
            .SetProperty(o => o.UpdatedAt, parkedAt), cancellationToken);
        return updated == 1;
    }

    public async Task<bool> ParkMalformedProcessingAsync(
        Guid tenantId,
        Guid outboxId,
        DateTime parkedAt,
        CancellationToken cancellationToken)
    {
        int updated = await dbContext.IntegrationSyncOutbox
            .IgnoreTenantFilter(TenantFilterBypassReasons.IntegrationSyncWorkerCrossTenantQueue)
            .Where(outbox => outbox.TenantId == tenantId &&
                outbox.Id == outboxId &&
                outbox.Status == IntegrationSyncStatus.Processing &&
                (outbox.ProcessingStartedAt == null || outbox.ProcessingLeaseToken == null))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(outbox => outbox.Status, IntegrationSyncStatus.DeadLettered)
                .SetProperty(outbox => outbox.LastFailureAt, parkedAt)
                .SetProperty(outbox => outbox.LastError, IntegrationSyncFailureCodes.ProviderOutcomeAmbiguous)
                .SetProperty(outbox => outbox.NextAttemptAt, (DateTime?)null)
                .SetProperty(outbox => outbox.DeadLetteredAt, parkedAt)
                .SetProperty(outbox => outbox.ProcessingStartedAt, (DateTime?)null)
                .SetProperty(outbox => outbox.ProcessingLeaseToken, (Guid?)null)
                .SetProperty(outbox => outbox.UpdatedAt, parkedAt), cancellationToken);
        return updated == 1;
    }

    public async Task<IntegrationSyncOutbox?> ResolveAmbiguousAsync(
        IntegrationSyncRecoveryRequest request,
        CancellationToken cancellationToken)
    {
        string failureCode = request.Decision switch
        {
            IntegrationSyncRecoveryDecision.ConfirmAccepted => IntegrationSyncFailureCodes.OperatorConfirmedAccepted,
            IntegrationSyncRecoveryDecision.RetryDefinitelyNotAccepted => IntegrationSyncFailureCodes.OperatorRetryDefinitelyNotAccepted,
            IntegrationSyncRecoveryDecision.DeadLetter => IntegrationSyncFailureCodes.OperatorDeadLettered,
            _ => throw new ArgumentOutOfRangeException(nameof(request))
        };
        IntegrationSyncStatus status = request.Decision == IntegrationSyncRecoveryDecision.ConfirmAccepted
            ? IntegrationSyncStatus.Completed
            : request.Decision == IntegrationSyncRecoveryDecision.RetryDefinitelyNotAccepted
                ? IntegrationSyncStatus.RetryScheduled
                : IntegrationSyncStatus.DeadLettered;
        int updated = await dbContext.IntegrationSyncOutbox
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .Where(outbox => outbox.TenantId == request.TenantId &&
                outbox.Id == request.OutboxId &&
                outbox.Status == IntegrationSyncStatus.DeadLettered &&
                outbox.LastError == IntegrationSyncFailureCodes.ProviderOutcomeAmbiguous)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(outbox => outbox.Status, status)
                .SetProperty(outbox => outbox.CompletedAt,
                    request.Decision == IntegrationSyncRecoveryDecision.ConfirmAccepted ? request.ResolvedAt : (DateTime?)null)
                .SetProperty(outbox => outbox.NextAttemptAt,
                    request.Decision == IntegrationSyncRecoveryDecision.RetryDefinitelyNotAccepted ? request.ResolvedAt : (DateTime?)null)
                .SetProperty(outbox => outbox.DeadLetteredAt,
                    request.Decision == IntegrationSyncRecoveryDecision.DeadLetter ? request.ResolvedAt : (DateTime?)null)
                .SetProperty(outbox => outbox.LastError, failureCode)
                .SetProperty(outbox => outbox.CorrelationId, request.EvidenceReference)
                .SetProperty(outbox => outbox.UpdatedBy, request.ActorId)
                .SetProperty(outbox => outbox.UpdatedAt, request.ResolvedAt), cancellationToken);
        if (updated != 1)
        {
            return null;
        }

        return await dbContext.IntegrationSyncOutbox
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .SingleAsync(outbox => outbox.TenantId == request.TenantId && outbox.Id == request.OutboxId, cancellationToken);
    }

    private IQueryable<IntegrationSyncOutbox> ActiveClaim(IntegrationSyncClaimIdentity claim) =>
        dbContext.IntegrationSyncOutbox
            .IgnoreTenantFilter(TenantFilterBypassReasons.IntegrationSyncWorkerCrossTenantQueue)
            .Where(o => o.TenantId == claim.TenantId &&
                o.Id == claim.OutboxId &&
                o.Status == IntegrationSyncStatus.Processing &&
                o.ProcessingLeaseToken == claim.LeaseToken &&
                o.ProcessingStartedAt == claim.ProcessingStartedAt);
}
