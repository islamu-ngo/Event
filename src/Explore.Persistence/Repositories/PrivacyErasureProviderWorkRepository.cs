using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class PrivacyErasureProviderWorkRepository(ExploreDbContext dbContext)
    : IPrivacyErasureProviderWorkRepository
{
    public async Task<int> AddMissingAsync(
        IReadOnlyCollection<PrivacyErasureProviderWork> work,
        CancellationToken cancellationToken)
    {
        if (work.Count == 0)
        {
            return 0;
        }

        PrivacyErasureProviderWork[] distinct = work
            .DistinctBy(item => (item.IntentId, item.ProviderKind, item.Action, item.TenantId, item.TargetId))
            .ToArray();
        Guid[] intentIds = distinct.Select(item => item.IntentId).Distinct().ToArray();
        PrivacyErasureProviderWork[] existing = await dbContext.PrivacyErasureProviderWork
            .AsNoTracking()
            .Where(item => intentIds.Contains(item.IntentId))
            .ToArrayAsync(cancellationToken);
        PrivacyErasureProviderWork[] missing = distinct
            .Where(candidate => !existing.Any(item =>
                item.IntentId == candidate.IntentId
                && item.ProviderKind == candidate.ProviderKind
                && item.Action == candidate.Action
                && item.TenantId == candidate.TenantId
                && item.TargetId == candidate.TargetId))
            .ToArray();
        await dbContext.PrivacyErasureProviderWork.AddRangeAsync(missing, cancellationToken);
        return distinct.Length;
    }

    public async Task<int> ExpireLocatorsAsync(
        DateTime utcNow,
        int batchSize,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            PrivacyErasureProviderWork[] expired = await dbContext.PrivacyErasureProviderWork
                .Where(item => item.ProtectedLocator != null && item.LocatorExpiresAtUtc <= utcNow)
                .OrderBy(item => item.LocatorExpiresAtUtc)
                .ThenBy(item => item.Id)
                .Take(batchSize)
                .ToArrayAsync(cancellationToken);
            if (!dryRun)
            {
                foreach (PrivacyErasureProviderWork item in expired)
                {
                    item.ExpireLocator(utcNow);
                }

                if (expired.Length != 0)
                {
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
            }

            await transaction.CommitAsync(cancellationToken);

            return expired.Length;
        });
    }

    public async Task<IReadOnlyList<PrivacyErasureProviderWork>> ClaimDueAsync(
        string leaseOwner,
        int batchSize,
        DateTime claimedAtUtc,
        DateTime leaseExpiresAtUtc,
        CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            PrivacyErasureProviderWork[] due = await dbContext.PrivacyErasureProviderWork
                .Where(item => item.ProtectedLocator != null
                    && item.LocatorExpiresAtUtc > claimedAtUtc
                    && (((item.Status == PrivacyErasureProviderWorkStatus.Pending
                        || item.Status == PrivacyErasureProviderWorkStatus.RetryScheduled)
                        && item.NextAttemptAtUtc <= claimedAtUtc)
                    || (item.Status == PrivacyErasureProviderWorkStatus.Processing
                        && item.LeaseExpiresAtUtc <= claimedAtUtc)))
                .OrderBy(item => item.NextAttemptAtUtc)
                .ThenBy(item => item.Id)
                .Take(batchSize)
                .ToArrayAsync(cancellationToken);
            foreach (PrivacyErasureProviderWork item in due)
            {
                item.Claim(leaseOwner, Guid.CreateVersion7(), claimedAtUtc, leaseExpiresAtUtc);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return due;
        });
    }

    public Task<bool> TryMarkSucceededAsync(
        Guid id,
        long fenceToken,
        Guid leaseToken,
        DateTime completedAtUtc,
        CancellationToken cancellationToken) =>
        TrySettleAsync(
            id,
            fenceToken,
            leaseToken,
            item => item.MarkSucceeded(fenceToken, leaseToken, completedAtUtc),
            cancellationToken);

    public Task<bool> TryMarkUnknownAsync(
        Guid id,
        long fenceToken,
        Guid leaseToken,
        DateTime unknownAtUtc,
        string failureCode,
        CancellationToken cancellationToken) =>
        TrySettleAsync(
            id,
            fenceToken,
            leaseToken,
            item => item.MarkUnknown(fenceToken, leaseToken, unknownAtUtc, failureCode),
            cancellationToken);

    public async Task<bool> TryReconcileUnknownAsync(
        Guid id,
        long fenceToken,
        PrivacyErasureProviderReconciliation outcome,
        DateTime reconciledAtUtc,
        CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            PrivacyErasureProviderWork? item = await dbContext.PrivacyErasureProviderWork.SingleOrDefaultAsync(
                candidate => candidate.Id == id
                    && candidate.LeaseFence == fenceToken
                    && candidate.Status == PrivacyErasureProviderWorkStatus.Unknown,
                cancellationToken);
            if (item is null)
            {
                return false;
            }

            item.Reconcile(outcome, reconciledAtUtc);
            if (outcome == PrivacyErasureProviderReconciliation.Completed)
            {
                PrivacyErasureSaga saga = await dbContext.PrivacyErasureSagas.SingleAsync(
                    candidate => candidate.IntentId == item.IntentId,
                    cancellationToken);
                saga.MarkProviderWorkCompleted(reconciledAtUtc, saga.ConcurrencyToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        });
    }

    public Task<bool> TryScheduleRetryAsync(
        Guid id,
        long fenceToken,
        Guid leaseToken,
        DateTime failedAtUtc,
        DateTime nextAttemptAtUtc,
        string failureCode,
        CancellationToken cancellationToken) =>
        TrySettleAsync(
            id,
            fenceToken,
            leaseToken,
            item => item.ScheduleRetry(fenceToken, leaseToken, failedAtUtc, nextAttemptAtUtc, failureCode),
            cancellationToken);

    public Task<bool> TryDeadLetterAsync(
        Guid id,
        long fenceToken,
        Guid leaseToken,
        DateTime failedAtUtc,
        string failureCode,
        CancellationToken cancellationToken) =>
        TrySettleAsync(
            id,
            fenceToken,
            leaseToken,
            item => item.DeadLetter(fenceToken, leaseToken, failedAtUtc, failureCode),
            cancellationToken);

    public Task<int> CountOutstandingAsync(Guid intentId, CancellationToken cancellationToken) =>
        dbContext.PrivacyErasureProviderWork
            .AsNoTracking()
            .CountAsync(
                item => item.IntentId == intentId
                    && item.Status != PrivacyErasureProviderWorkStatus.Completed,
                cancellationToken);

    public Task<int> CountCompletedAsync(Guid intentId, CancellationToken cancellationToken) =>
        dbContext.PrivacyErasureProviderWork
            .AsNoTracking()
            .CountAsync(
                item => item.IntentId == intentId
                    && item.Status == PrivacyErasureProviderWorkStatus.Completed,
                cancellationToken);

    public Task<int> CountUnknownAsync(CancellationToken cancellationToken) =>
        CountStatusAsync(PrivacyErasureProviderWorkStatus.Unknown, cancellationToken);

    public Task<int> CountDeadLetteredAsync(CancellationToken cancellationToken) =>
        CountStatusAsync(PrivacyErasureProviderWorkStatus.DeadLettered, cancellationToken);

    public Task<int> CountDueAsync(DateTime nowUtc, CancellationToken cancellationToken) =>
        dbContext.PrivacyErasureProviderWork
            .AsNoTracking()
            .CountAsync(
                item => (item.Status == PrivacyErasureProviderWorkStatus.Pending
                        || item.Status == PrivacyErasureProviderWorkStatus.RetryScheduled)
                    && item.NextAttemptAtUtc <= nowUtc,
                cancellationToken);

    public async Task<int> CleanupCompletedAsync(
        DateTime cutoffUtc,
        int batchSize,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        PrivacyErasureProviderWork[] completed = await dbContext.PrivacyErasureProviderWork
            .Where(item => item.Status == PrivacyErasureProviderWorkStatus.Completed
                && item.CompletedAtUtc <= cutoffUtc)
            .OrderBy(item => item.CompletedAtUtc)
            .Take(batchSize)
            .ToArrayAsync(cancellationToken);
        if (!dryRun && completed.Length != 0)
        {
            dbContext.PrivacyErasureProviderWork.RemoveRange(completed);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return completed.Length;
    }

    private Task<int> CountStatusAsync(
        PrivacyErasureProviderWorkStatus status,
        CancellationToken cancellationToken) =>
        dbContext.PrivacyErasureProviderWork.AsNoTracking().CountAsync(item => item.Status == status, cancellationToken);

    private async Task<bool> TrySettleAsync(
        Guid id,
        long fenceToken,
        Guid leaseToken,
        Action<PrivacyErasureProviderWork> settle,
        CancellationToken cancellationToken)
    {
        PrivacyErasureProviderWork? item = await dbContext.PrivacyErasureProviderWork.SingleOrDefaultAsync(
            candidate => candidate.Id == id
                && candidate.LeaseFence == fenceToken
                && candidate.LeaseToken == leaseToken
                && candidate.Status == PrivacyErasureProviderWorkStatus.Processing,
            cancellationToken);
        if (item is null)
        {
            return false;
        }

        settle(item);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
