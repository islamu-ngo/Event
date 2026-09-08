using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace Explore.Persistence.Repositories;

public sealed partial class RegistrationPaymentAttemptRepository(ExploreDbContext dbContext) : IRegistrationPaymentAttemptRepository
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    private async Task ClosePendingMaterialChangeChoicesAsync(
        PaymentAttempt attempt,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        if (attempt.PaymentAttemptStatusId != (int)PaymentAttemptStatusEnum.Failed &&
            attempt.PaymentAttemptStatusId != (int)PaymentAttemptStatusEnum.Cancelled)
        {
            return;
        }

        List<RegistrationMaterialChangeChoice> choices = await dbContext.RegistrationMaterialChangeChoices
            .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationFinalizationWorkerCrossTenantQueue)
            .Where(value => value.TenantId == attempt.TenantId && value.PaymentAttemptId == attempt.Id &&
                            value.Status == MaterialChangeChoiceStatusEnum.Pending)
            .ToListAsync(cancellationToken);
        foreach (RegistrationMaterialChangeChoice choice in choices)
        {
            choice.MarkNotApplicable(observedAt);
        }
    }

    private Task<bool> ExecuteFencedTransactionAsync(
        Func<CancellationToken, Task<bool>> operation,
        CancellationToken cancellationToken) =>
        ExecuteFencedTransactionAsync<bool>(operation, cancellationToken);

    private async Task<T> ExecuteFencedTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                isolationLevel,
                cancellationToken);
            try
            {
                T result = await operation(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();
                return result;
            }
            catch
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
                finally
                {
                    dbContext.ChangeTracker.Clear();
                }

                throw;
            }
        });
    }

    private bool ClearTrackerWhenUpdated(int rows)
    {
        if (rows == 1)
        {
            dbContext.ChangeTracker.Clear();
            return true;
        }

        return false;
    }

    private Task<int> ClaimCandidate(
        CheckoutDispatchCandidate candidate,
        string leaseOwner,
        Guid leaseToken,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        long nextFence,
        CancellationToken cancellationToken)
    {
        DateTime leaseExpiresAt = claimedAt.Add(leaseDuration);
        IQueryable<CheckoutDispatchEffect> query = dbContext.CheckoutDispatchEffects
            .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationFinalizationWorkerCrossTenantQueue)
            .Where(value =>
                value.TenantId == candidate.TenantId &&
                value.Id == candidate.EffectId &&
                value.RegistrationOrderId == candidate.RegistrationOrderId &&
                value.PaymentAttemptId == candidate.PaymentAttemptId &&
                value.Status == candidate.Status &&
                value.ProcessingFence == candidate.ProcessingFence &&
                value.AttemptCount == candidate.AttemptCount);

        query = candidate.Status == OutboxMessageStatus.Processing
            ? query.Where(value => value.ProcessingLeaseToken == candidate.ProcessingLeaseToken && value.ProcessingLeaseExpiresAt == candidate.ProcessingLeaseExpiresAt && value.ProcessingLeaseExpiresAt <= claimedAt)
            : query.Where(value => value.ProcessingLeaseToken == null && value.ProcessingLeaseExpiresAt == null && value.NextAttemptAt == candidate.NextAttemptAt && (value.NextAttemptAt == null || value.NextAttemptAt <= claimedAt));

        return query.ExecuteUpdateAsync(setters => setters
            .SetProperty(value => value.Status, OutboxMessageStatus.Processing)
            .SetProperty(value => value.ProcessingLeaseOwner, leaseOwner)
            .SetProperty(value => value.ProcessingLeaseToken, leaseToken)
            .SetProperty(value => value.ProcessingLeaseExpiresAt, leaseExpiresAt)
            .SetProperty(value => value.ProcessingFence, nextFence)
            .SetProperty(value => value.AttemptCount, candidate.AttemptCount + 1)
            .SetProperty(value => value.NextAttemptAt, (DateTime?)null)
            .SetProperty(value => value.UpdatedAt, claimedAt), cancellationToken);
    }

    private sealed record CheckoutDispatchCandidate(
        Guid EffectId,
        Guid TenantId,
        Guid RegistrationOrderId,
        Guid PaymentAttemptId,
        OutboxMessageStatus Status,
        DateTime? NextAttemptAt,
        Guid? ProcessingLeaseToken,
        DateTime? ProcessingLeaseExpiresAt,
        long ProcessingFence,
        int AttemptCount,
        DateTime? UnknownAt,
        string? LastFailureCode);
}
