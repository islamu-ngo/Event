
using System.Data;
using Explore.Application.Contracts.Identity;
using Explore.Application.Models;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Identity;

internal sealed class LocalIdentityLifecycleDeliveryStore(
    DbContext identityDbContext,
    ExploreDbContext applicationDbContext,
    LocalIdentityCredentialStateStore credentialStates,
    TimeProvider timeProvider) : ILocalIdentityLifecycleDeliveryStore
{
    public async Task<LocalIdentityLifecycleRequest?> ReadRequestAsync(Guid localSubjectId, Guid externalLoginId,
        LocalIdentityLifecyclePurpose purpose, string? proposedAddress, string? expectedSecurityStamp,
        CancellationToken cancellationToken)
    {
        var binding = await credentialStates.ReadLinkedIdentityAsync(localSubjectId, cancellationToken);
        if (binding is null || binding.ExternalLoginId != externalLoginId || binding.CredentialState != LocalCredentialState.Ready)
            return null;
        var user = await identityDbContext.Set<LocalIdentityUser>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == localSubjectId, cancellationToken);
        if (user is null) return null;
        string? address = purpose == LocalIdentityLifecyclePurpose.EmailChange ? proposedAddress : user.Email;
        return string.IsNullOrWhiteSpace(address) ? null : new LocalIdentityLifecycleRequest(localSubjectId,
            binding.PersonalActorId, externalLoginId, purpose, address, expectedSecurityStamp);
    }

    public async Task<IReadOnlyList<LocalIdentityLifecyclePointer>> ReadPendingAsync(int maximumCount, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCount);
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        await identityDbContext.Set<LocalIdentityLifecycleOperation>()
            .Where(row => row.DeliveryState == LocalIdentityLifecycleDeliveryState.Pending
                && (row.ExpiresAt <= now || row.ConsumedAt != null))
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.DeliveryState, LocalIdentityLifecycleDeliveryState.Failed), cancellationToken);
        var rows = await identityDbContext.Set<LocalIdentityLifecycleOperation>().AsNoTracking()
            .Where(row => row.DeliveryState == LocalIdentityLifecycleDeliveryState.Pending && row.CreatedAt <= now && row.ExpiresAt > now
                && row.ConsumedAt == null)
            .OrderBy(row => row.DeliveryAttemptCount).ThenBy(row => row.CreatedAt).ThenBy(row => row.Id)
            .Take(maximumCount).ToListAsync(cancellationToken);
        return rows.Select(row => row.Pointer()).ToArray();
    }

    public async Task<IReadOnlyList<LocalIdentityLifecyclePointer>> ReadPendingSynchronizationAsync(int maximumCount, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCount);
        // Receipt authority, unlike public token authority, remains repairable after link expiry.
        // Superseded native stamps cannot apply an older profile snapshot or starve current receipts.
        var rows = await (from operation in identityDbContext.Set<LocalIdentityLifecycleOperation>().AsNoTracking()
                          join user in identityDbContext.Set<LocalIdentityUser>().AsNoTracking() on operation.LocalSubjectId equals user.Id
                          where operation.ConsumedAt != null && operation.SynchronizedAt == null && operation.ResultSecurityStamp == user.SecurityStamp
                          orderby operation.ConsumedAt, operation.Id
                          select operation).Take(maximumCount).ToListAsync(cancellationToken);
        return rows.Select(row => row.Pointer()).ToArray();
    }

    public Task<bool> TryReserveGlobalSmtpAsync(int limitPerMinute, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limitPerMinute);
        // The UoW explicitly commits even denied reservations and rolls back failures, releasing provider-owned transaction locks.
        return new EfCoreUnitOfWork(applicationDbContext).ExecuteSerializableAsync(async token =>
        {
            await using var claim = await RelationalNamedLock.AcquireTransactionAsync(applicationDbContext,
                EmailDispatchOutboxRepository.ClaimAdvisoryLockName, cancellationToken);
            await using var rate = await RelationalNamedLock.AcquireTransactionAsync(applicationDbContext,
                "email-dispatch-smtp-rate", cancellationToken);
            DateTime now = await RelationalDatabaseClock.GetUtcNowAsync(applicationDbContext, cancellationToken);
            var state = await applicationDbContext.EmailDispatchProcessorStates.SingleOrDefaultAsync(
                row => row.ProcessorCode == EmailDispatchOutboxRepository.SmtpProcessorCode, cancellationToken)
                ?? new EmailDispatchProcessorState
                {
                    Id = Guid.CreateVersion7(),
                    ProcessorCode = EmailDispatchOutboxRepository.SmtpProcessorCode,
                    UpdatedAt = now
                };
            try
            {
                if (state.IsPaused) return false;
                int limit = state.GlobalSmtpRateLimitPerMinuteOverride ?? limitPerMinute;
                bool refill = state.SmtpAvailableTokens is null || state.SmtpRefillAt is null || now >= state.SmtpRefillAt;
                int available = refill ? limit : Math.Min(state.SmtpAvailableTokens!.Value, limit);
                if (available <= 0) return false;
                state.SmtpAvailableTokens = available - 1;
                if (refill) state.SmtpRefillAt = now.AddMinutes(1);
                state.UpdatedAt = now;
                if (applicationDbContext.Entry(state).State == EntityState.Detached)
                    applicationDbContext.Add(state);
                await applicationDbContext.SaveChangesAsync(token);
                return true;
            }
            finally { applicationDbContext.Entry(state).State = EntityState.Detached; }
        }, cancellationToken);
    }

    public Task<Guid?> TryAdmitAsync(LocalIdentityLifecyclePointer operation, CancellationToken cancellationToken) =>
        identityDbContext.Database.CreateExecutionStrategy().ExecuteAsync(() =>
            EfCoreUnitOfWork.ExecuteBootstrapConflictRetryAsync(() => AdmitAttemptAsync(operation, cancellationToken), cancellationToken));

    private async Task<Guid?> AdmitAttemptAsync(LocalIdentityLifecyclePointer pointer, CancellationToken cancellationToken)
    {
        await using var transaction = await identityDbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var row = await Exact(pointer).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        if (row is null || row.DeliveryState != LocalIdentityLifecycleDeliveryState.Pending
            || row.ConsumedAt is not null || row.CreatedAt > now || row.ExpiresAt <= now) return null;
        var binding = await credentialStates.ReadLinkedIdentityAsync(pointer.LocalSubjectId, cancellationToken);
        var metadata = await credentialStates.ReadAsync(pointer.LocalSubjectId, row.SecurityStamp, cancellationToken);
        var user = await identityDbContext.Set<LocalIdentityUser>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == pointer.LocalSubjectId && candidate.SecurityStamp == row.SecurityStamp, cancellationToken);
        DateTime cutoff = now.AddHours(-1);
        int attempts = await identityDbContext.Set<LocalIdentityLifecycleOperation>()
            .Where(candidate => candidate.LocalSubjectId == pointer.LocalSubjectId && candidate.Purpose == pointer.Purpose
                && candidate.DeliveryAdmittedAt >= cutoff).SumAsync(candidate => candidate.DeliveryAttemptCount, cancellationToken);
        if (binding is null || binding.PersonalActorId != pointer.PersonalActorId || binding.ExternalLoginId != pointer.ExternalLoginId
            || binding.CredentialState != LocalCredentialState.Ready || metadata?.OperationId != row.CredentialOperationId
            || user is null || attempts >= 3)
        {
            await Exact(pointer).Where(candidate => candidate.DeliveryState == LocalIdentityLifecycleDeliveryState.Pending)
                .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.DeliveryState, LocalIdentityLifecycleDeliveryState.Failed), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }
        // The same native CAS as intake serializes per-account budget against concurrent credential and lifecycle writes.
        int users = await identityDbContext.Set<LocalIdentityUser>()
            .Where(candidate => candidate.Id == user.Id && candidate.SecurityStamp == row.SecurityStamp && candidate.ConcurrencyStamp == user.ConcurrencyStamp)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.ConcurrencyStamp, Guid.CreateVersion7().ToString("N")), cancellationToken);
        Guid attemptId = Guid.CreateVersion7();
        int operations = await Exact(pointer).Where(candidate => candidate.DeliveryState == LocalIdentityLifecycleDeliveryState.Pending
                && candidate.ConsumedAt == null && candidate.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.DeliveryState, LocalIdentityLifecycleDeliveryState.Unknown)
                .SetProperty(candidate => candidate.DeliveryAttemptId, attemptId)
                .SetProperty(candidate => candidate.DeliveryAttemptCount, candidate => candidate.DeliveryAttemptCount + 1)
                .SetProperty(candidate => candidate.DeliveryAdmittedAt, now)
                .SetProperty(candidate => candidate.DeliveryCompletedAt, (DateTime?)null), cancellationToken);
        if (users != 1 || operations != 1) return null;
        await transaction.CommitAsync(cancellationToken);
        return attemptId;
    }

    public async Task CompleteAsync(LocalIdentityLifecyclePointer operation, Guid attemptId, SmtpDeliveryOutcome outcome,
        CancellationToken cancellationToken)
    {
        if (outcome == SmtpDeliveryOutcome.Uncertain) return;
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        await Exact(operation).Where(row => row.DeliveryState == LocalIdentityLifecycleDeliveryState.Unknown && row.DeliveryAttemptId == attemptId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.DeliveryState, row => outcome == SmtpDeliveryOutcome.Accepted
                    ? LocalIdentityLifecycleDeliveryState.Accepted
                    : outcome == SmtpDeliveryOutcome.TransientFailure && row.DeliveryAttemptCount < 3 && row.ExpiresAt > now
                        ? LocalIdentityLifecycleDeliveryState.Pending : LocalIdentityLifecycleDeliveryState.Failed)
                .SetProperty(row => row.DeliveryCompletedAt, now), cancellationToken);
    }

    private IQueryable<LocalIdentityLifecycleOperation> Exact(LocalIdentityLifecyclePointer pointer) =>
        identityDbContext.Set<LocalIdentityLifecycleOperation>().Where(row => row.Id == pointer.OperationId
            && row.LocalSubjectId == pointer.LocalSubjectId && row.PersonalActorId == pointer.PersonalActorId
            && row.ExternalLoginId == pointer.ExternalLoginId && row.Purpose == pointer.Purpose && row.Generation == pointer.Generation);
}
