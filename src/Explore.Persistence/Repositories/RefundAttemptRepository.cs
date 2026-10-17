using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Payments;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed class RefundAttemptRepository(ExploreDbContext dbContext) : IRefundAttemptRepository
{
    public Task<RefundAttempt?> TryBeginDispatchAsync(
        RefundAttempt expected, Guid dispatchToken, bool applicationFee, DateTime committedAt,
        DateTime mutationExpiresAt, OutboxMessage recoveryTrigger, CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Refund handoff ownership must commit outside an ambient transaction.");
        Guid stamp = expected.ConcurrencyStamp;
        return ExecuteLockedAsync<RefundAttempt?>(expected.TenantId, expected.PaymentAttemptId, async token =>
        {
            RefundAttempt? current = await LoadCurrentAsync(expected.TenantId, expected.Id, token);
            if (current is null || current.ConcurrencyStamp != stamp ||
                (applicationFee ? !current.CanDispatchApplicationFee : !current.CanDispatchBuyer) ||
                current.ProviderMutationExpiresAt <= committedAt)
                return null;

            current.BeginDispatch(dispatchToken, applicationFee, committedAt, mutationExpiresAt);
            dbContext.OutboxMessages.Add(recoveryTrigger);
            await dbContext.SaveChangesAsync(token);
            return current;
        }, cancellationToken);
    }

    public Task<RefundAttempt?> SettleAndScheduleAsync(
        RefundAttempt expected, Guid? dispatchToken, RefundProviderResult result, DateTime observedAt,
        OutboxMessage recoveryTrigger, CancellationToken cancellationToken)
    {
        Guid stamp = expected.ConcurrencyStamp;
        return ExecuteLockedAsync<RefundAttempt?>(expected.TenantId, expected.PaymentAttemptId, async token =>
        {
            RefundAttempt? current = await LoadCurrentAsync(expected.TenantId, expected.Id, token);
            if (current is null ||
                current.Status is RefundAttemptStatusEnum.Succeeded or RefundAttemptStatusEnum.Failed or RefundAttemptStatusEnum.Cancelled)
                return current;
            if (current.ConcurrencyStamp != stamp ||
                dispatchToken.HasValue && current.DispatchToken != dispatchToken)
                return null;

            if (result.Outcome == RefundProviderOutcome.Observed && result.Observation is not null)
            {
                if (dispatchToken.HasValue &&
                    current.DispatchPhase == RefundDispatchPhase.ApplicationFeeHandoffCommitted &&
                    result.Observation.ApplicationFeeHandoffStarted == false)
                    current.ProveNoProviderHandoff(dispatchToken.Value, observedAt);
                RefundAttemptEvidence.Apply(current, result.Observation, observedAt, result.ProviderRequestId);
            }
            else
            {
                if (dispatchToken.HasValue && result.Failure?.ProviderHandoffStarted == false)
                    current.ProveNoProviderHandoff(dispatchToken.Value, observedAt);
                else
                    current.MarkUnknown(observedAt, result.ProviderRequestId);

                if (result.Outcome == RefundProviderOutcome.Failed && result.Failure is not null)
                    current.MarkProviderBlocked(observedAt, result.ProviderRequestId, result.Failure.Code);
            }

            // Outcome and its next native work commit together. This also survives an
            // acknowledgement lost after SaveChanges/Commit and a worker dying before return.
            dbContext.OutboxMessages.Add(recoveryTrigger);
            await dbContext.SaveChangesAsync(token);
            return current;
        }, cancellationToken, allowReadCommittedAmbient: true);
    }

    private async Task<RefundAttempt?> LoadCurrentAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        RefundAttempt? current = await GetByIdAsync(tenantId, id, cancellationToken);
        if (current is not null)
            await dbContext.Entry(current).ReloadAsync(cancellationToken);
        return current;
    }

    public async Task<RefundReservationResult?> RecoverOperationAsync(
        Guid tenantId, Guid paymentAttemptId, string nativeOperationKey, string nativeInputDigest,
        CancellationToken cancellationToken)
    {
        RefundAttempt? existing = await dbContext.RefundAttempts.AsNoTracking()
            .Include(value => value.Lines)
            .SingleOrDefaultAsync(value => value.TenantId == tenantId &&
                value.NativeOperationKey == nativeOperationKey, cancellationToken);
        if (existing is null)
            return null;
        return existing.PaymentAttemptId == paymentAttemptId &&
               string.Equals(existing.NativeInputDigest, nativeInputDigest, StringComparison.Ordinal)
            ? new(RefundReservationDisposition.Duplicate, existing)
            : new(RefundReservationDisposition.OperationConflict, null);
    }

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
                            (existing.NativeOperationKey == attempt.NativeOperationKey ||
                             existing.ProviderIdempotencyKey == attempt.ProviderIdempotencyKey),
                cancellationToken);
        if (duplicate is not null)
        {
            bool exact = duplicate.NativeOperationKey == attempt.NativeOperationKey &&
                duplicate.NativeInputDigest == attempt.NativeInputDigest &&
                duplicate.RequestedAmountMinor == attempt.RequestedAmountMinor &&
                duplicate.CreatedBy == attempt.CreatedBy &&
                duplicate.PaymentAttemptId == attempt.PaymentAttemptId &&
                duplicate.RegistrationOrderId == attempt.RegistrationOrderId &&
                duplicate.PaidOrderAcceptanceSnapshotId == attempt.PaidOrderAcceptanceSnapshotId &&
                duplicate.SourceCampaignId == attempt.SourceCampaignId &&
                duplicate.ProviderCode == attempt.ProviderCode &&
                duplicate.ExternalAccountId == attempt.ExternalAccountId &&
                duplicate.ProviderPaymentId == attempt.ProviderPaymentId &&
                duplicate.ProviderIdempotencyKey == attempt.ProviderIdempotencyKey &&
                duplicate.CurrencyCode == attempt.CurrencyCode &&
                duplicate.AuthorityCode == attempt.AuthorityCode && duplicate.ReasonCode == attempt.ReasonCode;
            return exact
                ? new(RefundReservationDisposition.Duplicate, duplicate)
                : new(RefundReservationDisposition.OperationConflict, null);
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
        long requestedAmount = attempt.RequestedAmountMinor ??
            checked(payment.TotalMinor - existingAttempts.Where(value => value.ReservesCapacity)
                .Sum(value => value.Allocation.TotalMinor));
        if (requestedAmount <= 0)
            return new(RefundReservationDisposition.CapacityExceeded, null);
        try
        {
            RefundReservationRules.EnsureReservable(
                payment.TotalMinor,
                existingAttempts,
                disputes,
                requestedAmount);
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
        CancellationToken cancellationToken,
        bool allowReadCommittedAmbient = false)
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            IsolationLevel ambientIsolation = dbContext.Database.CurrentTransaction.GetDbTransaction().IsolationLevel;
            if (ambientIsolation != IsolationLevel.Serializable &&
                !(allowReadCommittedAmbient && ambientIsolation == IsolationLevel.ReadCommitted))
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
