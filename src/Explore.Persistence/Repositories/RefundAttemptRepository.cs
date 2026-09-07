using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed class RefundAttemptRepository(ExploreDbContext dbContext) : IRefundAttemptRepository
{
    public Task<RefundAttempt?> GetByIdAsync(
        Guid tenantId,
        Guid refundAttemptId,
        CancellationToken cancellationToken) =>
        dbContext.RefundAttempts
            .Include(attempt => attempt.Lines)
            .SingleOrDefaultAsync(
                attempt => attempt.TenantId == tenantId && attempt.Id == refundAttemptId,
                cancellationToken);

    public async Task<long> GetRefundableCapacityAsync(
        Guid tenantId,
        Guid paymentAttemptId,
        CancellationToken cancellationToken)
    {
        long? captured = await dbContext.PaymentAttempts
            .Where(payment => payment.TenantId == tenantId && payment.Id == paymentAttemptId &&
                              payment.PaymentAttemptStatusId == (int)PaymentAttemptStatusEnum.Succeeded)
            .Select(payment => (long?)payment.TotalMinor)
            .SingleOrDefaultAsync(cancellationToken);
        if (!captured.HasValue)
        {
            return 0;
        }

        long reserved = await dbContext.RefundAttempts
            .Where(attempt => attempt.TenantId == tenantId && attempt.PaymentAttemptId == paymentAttemptId &&
                              attempt.Status != RefundAttemptStatusEnum.Failed &&
                              attempt.Status != RefundAttemptStatusEnum.Cancelled)
            .SumAsync(attempt => (long?)attempt.Allocation.TotalMinor, cancellationToken) ?? 0;
        return Math.Max(0, checked(captured.Value - reserved));
    }

    public async Task<IReadOnlyList<PaymentDispute>> GetDisputesAsync(
        Guid tenantId,
        Guid paymentAttemptId,
        CancellationToken cancellationToken) =>
        await dbContext.PaymentDisputes
            .AsNoTracking()
            .Where(dispute => dispute.TenantId == tenantId && dispute.PaymentAttemptId == paymentAttemptId)
            .OrderBy(dispute => dispute.CreatedAt)
            .ThenBy(dispute => dispute.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RefundAttempt>> GetByPaymentAsync(
        Guid tenantId,
        Guid paymentAttemptId,
        CancellationToken cancellationToken) =>
        await dbContext.RefundAttempts
            .AsNoTracking()
            .Include(attempt => attempt.Lines)
            .Where(attempt => attempt.TenantId == tenantId && attempt.PaymentAttemptId == paymentAttemptId)
            .OrderBy(attempt => attempt.CreatedAt)
            .ThenBy(attempt => attempt.Id)
            .ToListAsync(cancellationToken);

    public Task<PaidOrderAcceptanceSnapshot?> GetAcceptanceAsync(
        Guid tenantId,
        Guid paidOrderAcceptanceSnapshotId,
        CancellationToken cancellationToken) =>
        dbContext.PaidOrderAcceptanceSnapshots
            .AsNoTracking()
            .Include(snapshot => snapshot.Lines)
            .SingleOrDefaultAsync(
                snapshot => snapshot.TenantId == tenantId &&
                            snapshot.Id == paidOrderAcceptanceSnapshotId,
                cancellationToken);

    public async Task<RefundReconciliationHealth> GetReconciliationHealthAsync(
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        IQueryable<RefundAttempt> attempts = dbContext.RefundAttempts
            .IgnoreTenantFilter(TenantFilterBypassReasons.RefundReconciliationHealthCrossTenantQueue)
            .AsNoTracking();
        IQueryable<RefundCampaign> campaigns = dbContext.RefundCampaigns
            .IgnoreTenantFilter(TenantFilterBypassReasons.RefundReconciliationHealthCrossTenantQueue)
            .AsNoTracking();
        IQueryable<PaymentDispute> disputes = dbContext.PaymentDisputes
            .IgnoreTenantFilter(TenantFilterBypassReasons.RefundReconciliationHealthCrossTenantQueue)
            .AsNoTracking();
        int pending = await attempts.CountAsync(value =>
            value.Status == RefundAttemptStatusEnum.Requested ||
            value.Status == RefundAttemptStatusEnum.DispatchPending ||
            value.Status == RefundAttemptStatusEnum.Pending, cancellationToken);
        int unknown = await attempts.CountAsync(value => value.Status == RefundAttemptStatusEnum.Unknown, cancellationToken);
        int requiresAction = await attempts.CountAsync(
            value => value.Status == RefundAttemptStatusEnum.RequiresAction, cancellationToken);
        DateTime recentFailureCutoff = observedAt.AddHours(-24);
        int failed = await attempts.CountAsync(value => value.Status == RefundAttemptStatusEnum.Failed &&
                                                       (value.UpdatedAt ?? value.LastObservedAt) >= recentFailureCutoff,
            cancellationToken);
        int operatorCampaigns = await campaigns.CountAsync(
            value => value.Status == RefundCampaignStatus.RequiresOperator, cancellationToken);
        DateTime dueSoonCutoff = observedAt.AddHours(24);
        int disputesOverdue = await disputes.CountAsync(value => value.Status == PaymentDisputeStatus.Open &&
                                                                 value.ResponseDueAt < observedAt,
            cancellationToken);
        int disputesDueSoon = await disputes.CountAsync(value => value.Status == PaymentDisputeStatus.Open &&
                                                                 value.ResponseDueAt >= observedAt &&
                                                                 value.ResponseDueAt <= dueSoonCutoff,
            cancellationToken);
        DateTime urgentCutoff = observedAt.AddHours(72);
        int disputesDueWithin72Hours = await disputes.CountAsync(value => value.Status == PaymentDisputeStatus.Open &&
                                                                          value.ResponseDueAt > dueSoonCutoff &&
                                                                          value.ResponseDueAt <= urgentCutoff,
            cancellationToken);
        DateTime? oldest = await attempts
            .Where(value => value.Status == RefundAttemptStatusEnum.Requested ||
                            value.Status == RefundAttemptStatusEnum.DispatchPending ||
                            value.Status == RefundAttemptStatusEnum.Pending ||
                            value.Status == RefundAttemptStatusEnum.RequiresAction ||
                            value.Status == RefundAttemptStatusEnum.Unknown)
            .MinAsync(value => (DateTime?)value.LastObservedAt, cancellationToken);
        return new(
            pending, unknown, requiresAction, failed, operatorCampaigns,
            disputesDueSoon, disputesDueWithin72Hours, disputesOverdue, oldest);
    }

    public Task<PaymentAttempt?> FindPaymentByProviderPaymentAsync(
        Guid tenantId,
        string externalAccountId,
        string providerPaymentId,
        CancellationToken cancellationToken) =>
        dbContext.PaymentAttempts.SingleOrDefaultAsync(
            payment => payment.TenantId == tenantId &&
                       payment.RecipientSnapshot.ExternalAccountId == externalAccountId &&
                       payment.ProviderPaymentId == providerPaymentId,
            cancellationToken);

    public Task<RefundReservationResult> ReserveAsync(
        RefundAttempt attempt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        return ExecuteLockedAsync(
            attempt.TenantId,
            attempt.PaymentAttemptId,
            token => ReserveLockedAsync(attempt, null, null, null, null, token),
            cancellationToken);
    }

    public Task<RefundReservationResult> ReserveAndScheduleAsync(
        RefundAttempt attempt,
        OutboxMessage dispatchTrigger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(dispatchTrigger);
        return ExecuteLockedAsync(
            attempt.TenantId,
            attempt.PaymentAttemptId,
            token => ReserveLockedAsync(attempt, dispatchTrigger, null, null, null, token),
            cancellationToken);
    }

    public Task<RefundReservationResult> ReserveAndRecordMaterialChangeRefundAsync(
        RefundAttempt attempt,
        Guid materialChangeChoiceId,
        Guid actorId,
        DateTime decidedAt,
        OutboxMessage dispatchTrigger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(dispatchTrigger);
        return ExecuteLockedAsync(
            attempt.TenantId,
            attempt.PaymentAttemptId,
            token => ReserveLockedAsync(
                attempt, dispatchTrigger, materialChangeChoiceId, actorId, decidedAt, token),
            cancellationToken);
    }

    public Task<PaymentDispute> ObserveDisputeAsync(
        PaymentDispute dispute,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dispute);
        return ExecuteLockedAsync(
            dispute.TenantId,
            dispute.PaymentAttemptId,
            token => ObserveDisputeLockedAsync(dispute, token),
            cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);

    public async Task<bool> RetryProviderBlockedAndScheduleAsync(
        RefundAttempt attempt,
        OutboxMessage reconciliationTrigger,
        DateTime requestedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(reconciliationTrigger);
        try
        {
            attempt.RetryProviderBlocked(requestedAt);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        await dbContext.OutboxMessages.AddAsync(reconciliationTrigger, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<RefundReservationResult> ReserveLockedAsync(
        RefundAttempt attempt,
        OutboxMessage? dispatchTrigger,
        Guid? materialChangeChoiceId,
        Guid? actorId,
        DateTime? decidedAt,
        CancellationToken cancellationToken)
    {
        RefundAttempt? duplicate = await dbContext.RefundAttempts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                existing => existing.TenantId == attempt.TenantId &&
                            existing.ProviderIdempotencyKey == attempt.ProviderIdempotencyKey,
                cancellationToken);
        if (duplicate is not null)
        {
            return new(RefundReservationDisposition.Duplicate, duplicate);
        }

        PaymentAttempt? payment = await dbContext.PaymentAttempts
            .Include(existing => existing.AcceptanceSnapshot)
            .AsNoTracking()
            .SingleOrDefaultAsync(
                existing => existing.TenantId == attempt.TenantId && existing.Id == attempt.PaymentAttemptId,
                cancellationToken);
        if (payment is null)
        {
            return new(RefundReservationDisposition.PaymentNotFound, null);
        }
        if (payment.PaymentAttemptStatusId != (int)PaymentAttemptStatusEnum.Succeeded || payment.ProviderPaymentId is null)
        {
            return new(RefundReservationDisposition.PaymentNotCaptured, null);
        }
        if (attempt.RegistrationOrderId != payment.RegistrationOrderId ||
            !string.Equals(attempt.ProviderCode, payment.ProviderCode, StringComparison.Ordinal) ||
            !string.Equals(attempt.ExternalAccountId, payment.RecipientSnapshot.ExternalAccountId, StringComparison.Ordinal) ||
            !string.Equals(attempt.CurrencyCode, payment.CurrencyCode, StringComparison.Ordinal) ||
            !string.Equals(attempt.ProviderPaymentId, payment.ProviderPaymentId, StringComparison.Ordinal) ||
            payment.AcceptanceSnapshot is null ||
            attempt.PaidOrderAcceptanceSnapshotId != payment.PaidOrderAcceptanceSnapshotId ||
            attempt.RefundPolicyVersion != payment.AcceptanceSnapshot.RefundPolicyVersion ||
            !string.Equals(attempt.RefundPolicyText, payment.AcceptanceSnapshot.RefundPolicyText, StringComparison.Ordinal) ||
            !string.Equals(attempt.RefundPolicyLanguageTag, payment.AcceptanceSnapshot.RefundPolicyLanguageTag, StringComparison.Ordinal))
        {
            return new(RefundReservationDisposition.AuthorityMismatch, null);
        }

        List<PaymentDispute> disputes = await dbContext.PaymentDisputes
            .AsNoTracking()
            .Where(dispute => dispute.TenantId == attempt.TenantId && dispute.PaymentAttemptId == attempt.PaymentAttemptId)
            .ToListAsync(cancellationToken);
        if (disputes.Any(dispute => dispute.IsOpen))
        {
            return new(RefundReservationDisposition.OpenDispute, null);
        }

        List<RefundAttempt> existingAttempts = await dbContext.RefundAttempts
            .AsNoTracking()
            .Include(existing => existing.Lines)
            .Where(existing => existing.TenantId == attempt.TenantId && existing.PaymentAttemptId == attempt.PaymentAttemptId)
            .ToListAsync(cancellationToken);
        try
        {
            RefundReservationRules.EnsureReservable(
                payment.TotalMinor,
                existingAttempts,
                disputes,
                attempt.Allocation.TotalMinor);
        }
        catch (InvalidOperationException)
        {
            return new(RefundReservationDisposition.CapacityExceeded, null);
        }

        attempt.ReallocateForReservation(existingAttempts, payment.AcceptanceSnapshot);

        if (materialChangeChoiceId.HasValue)
        {
            RegistrationMaterialChangeChoice? choice = await dbContext.RegistrationMaterialChangeChoices
                .SingleOrDefaultAsync(value => value.TenantId == attempt.TenantId &&
                                               value.Id == materialChangeChoiceId.Value,
                    cancellationToken);
            if (choice is null || choice.RefundCampaignId != attempt.SourceCampaignId ||
                choice.PaymentAttemptId != attempt.PaymentAttemptId ||
                choice.PaidOrderAcceptanceSnapshotId != attempt.PaidOrderAcceptanceSnapshotId ||
                !actorId.HasValue || !decidedAt.HasValue)
            {
                dbContext.ChangeTracker.Clear();
                return new(RefundReservationDisposition.AuthorityMismatch, null);
            }
            try
            {
                choice.RequestRefund(actorId.Value, decidedAt.Value);
            }
            catch (InvalidOperationException)
            {
                dbContext.ChangeTracker.Clear();
                return new(RefundReservationDisposition.MaterialChangeChoiceConflict, null);
            }
        }
        dbContext.RefundAttempts.Add(attempt);
        if (dispatchTrigger is not null)
        {
            dbContext.OutboxMessages.Add(dispatchTrigger);
        }
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException) when (materialChangeChoiceId.HasValue)
        {
            dbContext.ChangeTracker.Clear();
            return new(RefundReservationDisposition.MaterialChangeChoiceConflict, null);
        }
        return new(RefundReservationDisposition.Reserved, attempt);
    }

    private async Task<PaymentDispute> ObserveDisputeLockedAsync(
        PaymentDispute dispute,
        CancellationToken cancellationToken)
    {
        PaymentDispute? duplicate = await dbContext.PaymentDisputes
            .SingleOrDefaultAsync(
                existing => existing.TenantId == dispute.TenantId &&
                            existing.ProviderDisputeId == dispute.ProviderDisputeId,
                cancellationToken);
        if (duplicate is not null)
        {
            if (duplicate.PaymentAttemptId != dispute.PaymentAttemptId)
            {
                throw new InvalidOperationException("Dispute identity cannot move between payments.");
            }
            if (duplicate.ApplyProviderEvidence(
                dispute.Stage,
                dispute.Status,
                dispute.AmountMinor,
                dispute.CurrencyCode,
                dispute.LastObservedAt,
                dispute.ResponseDueAt))
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            return duplicate;
        }

        PaymentAttempt payment = await dbContext.PaymentAttempts
            .AsNoTracking()
            .SingleAsync(
                existing => existing.TenantId == dispute.TenantId && existing.Id == dispute.PaymentAttemptId,
                cancellationToken);
        if (!string.Equals(dispute.CurrencyCode, payment.CurrencyCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Dispute currency must match the captured payment.");
        }

        dbContext.PaymentDisputes.Add(dispute);
        await dbContext.SaveChangesAsync(cancellationToken);
        return dispute;
    }

    private async Task<T> ExecuteLockedAsync<T>(
        Guid tenantId,
        Guid paymentAttemptId,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            if (dbContext.Database.CurrentTransaction
                    .GetDbTransaction()
                    .IsolationLevel != IsolationLevel.Serializable)
            {
                throw new InvalidOperationException(
                    "Ambient refund reservation requires a serializable transaction.");
            }

            await using IAsyncDisposable ambientLock =
                await RelationalNamedLock.AcquireTransactionAsync(
                    dbContext,
                    $"refund-capacity:{tenantId:N}:{paymentAttemptId:N}",
                    cancellationToken);
            return await operation(cancellationToken);
        }

        string providerName = dbContext.Database.ProviderName
            ?? throw new InvalidOperationException("Refund persistence requires a relational database provider.");
        IsolationLevel isolationLevel = providerName == RelationalNamedLock.PostgreSqlProvider
            ? IsolationLevel.ReadCommitted
            : IsolationLevel.Serializable;
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(isolationLevel, cancellationToken);
            try
            {
                await using IAsyncDisposable paymentLock = await RelationalNamedLock.AcquireTransactionAsync(
                    dbContext,
                    $"refund-capacity:{tenantId:N}:{paymentAttemptId:N}",
                    cancellationToken);
                T result = await operation(cancellationToken);

                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                dbContext.ChangeTracker.Clear();
                throw;
            }
        });
    }
}
