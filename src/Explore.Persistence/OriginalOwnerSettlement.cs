using System.Data;
using System.Data.Common;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence;

/// <summary>Internal physical-owner machinery; no business classifier or journal authority lives here.</summary>
internal sealed class OriginalOwnerSettlement(ExploreDbContext context)
{
    private Attempt? _latest;
    private bool _active;

    internal async Task<IOriginalOwnerSettlement<T>> ExecuteAsync<T>(object invocation,
        Func<Attempt, Task<OriginalOwnerDecision<T>>> executeOwned, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        cancellationToken.ThrowIfCancellationRequested();
        RequireIdle();
        if (_active || ExecutionStrategy.Current?.RetriesOnFailure == true)
            throw new InvalidOperationException("Physical settlement cannot borrow or replay an outer owner.");
        var attempt = new Attempt(invocation, context.ContextId, context.TenantFilterTenantId,
            RelationalConnectionIdentity.Capture(context));
        _latest = attempt;
        _active = true;
        try
        {
            var decision = await new SingleAttemptStrategy(context).ExecuteAsync(() => executeOwned(attempt));
            // executeOwned must dispose the transaction and every associated enlistment/context
            // before returning. No receipt is visible to operation or disposal callbacks.
            RequireIdle();
            RequireContext(attempt);
            if (!attempt.SettledNormally || attempt.PhysicalTransaction is null)
                throw new InvalidOperationException("The physical owner did not acknowledge settlement.");
            return new Receipt<T>(this, attempt, decision.Value, decision.Disposition);
        }
        catch (Exception exception) when (exception is not HumanMutationOutcomeUnknownException
            && (attempt.CommitStarted || attempt.SettledNormally))
        {
            // Includes post-commit/rollback disposal and publication failures. A completed
            // transaction must never become a rejection or replay receipt through this path.
            ClearFailedState();
            throw new HumanMutationOutcomeUnknownException(exception);
        }
        finally
        {
            _active = false;
            context.ResetHumanCommitAdmissions();
            context.ResetTrustedSetupSettingsAdmissions();
        }
    }

    internal async Task<OriginalOwnerDecision<T>> SettleAsync<T>(Attempt attempt,
        IDbContextTransaction transaction, Func<CancellationToken, Task<OriginalOwnerDecision<T>>> operation,
        CancellationToken cancellationToken)
    {
        if (!_active || !ReferenceEquals(_latest, attempt) || attempt.PhysicalTransaction is not null)
            throw new InvalidOperationException("A settlement attempt can bind one physical transaction only.");
        attempt.PhysicalTransaction = transaction.GetDbTransaction();
        bool rollbackStarted = false;
        try
        {
            RequirePhysicalOwner(attempt);
            context.HumanCommitAttemptOwnsTransaction = true;
            var decision = await operation(cancellationToken)
                ?? throw new InvalidOperationException("The callback must propose a physical disposition.");
            cancellationToken.ThrowIfCancellationRequested();
            RequirePhysicalOwner(attempt);
            if (context.IdentityFenceTransactionFailed || context.StorageReferenceTransactionFailed)
                throw new ConcurrencyConflictException(ConcurrencyConflictException.ConcurrentUpdate,
                    "The original transaction has failed.");
            if (decision.Disposition == OriginalOwnerDisposition.WholeTransactionRolledBack)
            {
                // Deliberately no business/source classification here. Rejected intermediate
                // state need not satisfy success predicates; fresh evidence admission is separate.
                rollbackStarted = true;
                await transaction.RollbackAsync(CancellationToken.None);
                ClearFailedState();
            }
            else
            {
                await context.FlushDisclosureAsync(cancellationToken);
                await context.ValidateTrustedSetupSettingsSourcesAsync(cancellationToken);
                await context.ValidateHumanCommitAdmissionsAsync(cancellationToken);
                context.ValidateTrustedSetupSettingsTimes();
                RequirePhysicalOwner(attempt);
                attempt.CommitStarted = true;
                await transaction.CommitAsync(cancellationToken);
            }
            attempt.SettledNormally = true;
            return decision;
        }
        catch (Exception exception)
        {
            bool rollbackAcknowledged = false;
            if (!rollbackStarted && IsPhysicalOwner(attempt))
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    rollbackAcknowledged = true;
                }
                catch (Exception)
                {
                    // Any failed acknowledgement withholds settlement, including cancellation
                    // or interceptor failures. The original failure remains the diagnostic cause.
                }
            }
            ClearFailedState();
            if (attempt.CommitStarted || !rollbackAcknowledged)
                throw new HumanMutationOutcomeUnknownException(exception);
            if (ContainsCallbackReceipt(exception))
                throw new InvalidOperationException("A callback cannot certify its original owner's settlement.");
            throw;
        }
        finally
        {
            context.ResetHumanCommitAdmissions();
            context.ResetTrustedSetupSettingsAdmissions();
        }
    }

    internal T Consume<T>(object invocation, IOriginalOwnerSettlement<T> settlement,
        OriginalOwnerDisposition expectedDisposition)
    {
        RequireIdle();
        if (_active || settlement is not Receipt<T> receipt || !ReferenceEquals(receipt.Owner, this)
            || !ReferenceEquals(receipt.Attempt, _latest)
            || !ReferenceEquals(receipt.Attempt.Invocation, invocation)
            || receipt.Disposition != expectedDisposition)
            throw new InvalidOperationException("Settlement does not belong to this original owner and invocation.");
        RequireContext(receipt.Attempt);
        if (Interlocked.Exchange(ref receipt.Consumed, 1) != 0)
            throw new InvalidOperationException("The original settlement has already been consumed.");
        return receipt.Value;
    }

    private void RequireIdle()
    {
        context.ValidateTenantLease();
        if (!context.Database.IsRelational() || !context.HasOriginalCommitContextLease
            || context.IsTenantFilterBypassed || context.IdentityFenceOwnsTransaction
            || context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Physical settlement requires an idle original context lease.");
    }

    private void RequireContext(Attempt attempt)
    {
        context.ValidateTenantLease();
        if (context.ContextId != attempt.ContextLease || !context.HasOriginalCommitContextLease
            || context.TenantFilterTenantId != attempt.Tenant || context.IsTenantFilterBypassed
            || !attempt.Connection.Matches(context))
            throw new InvalidOperationException("Settlement cannot move to another context lease, tenant or connection.");
    }

    private bool IsPhysicalOwner(Attempt attempt)
    {
        try
        {
            return attempt.PhysicalTransaction is { } transaction
                && ReferenceEquals(context.Database.CurrentTransaction?.GetDbTransaction(), transaction)
                && attempt.Connection.Owns(transaction);
        }
        catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException)
        {
            return false;
        }
    }

    private void RequirePhysicalOwner(Attempt attempt)
    {
        RequireContext(attempt);
        if (!IsPhysicalOwner(attempt) || attempt.PhysicalTransaction!.IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Settlement requires the original live Serializable physical owner.");
    }

    private void ClearFailedState()
    {
        context.ChangeTracker.Clear();
        context.ResetDisclosureMutations();
    }

    private static bool ContainsCallbackReceipt(Exception exception) =>
        exception is HumanMutationAttemptRolledBackException
        || exception is AggregateException aggregate && aggregate.InnerExceptions.Any(ContainsCallbackReceipt)
        || exception.InnerException is { } inner && ContainsCallbackReceipt(inner);

    internal sealed class Attempt(object invocation, DbContextId contextLease, Guid? tenant, RelationalConnectionIdentity connection)
    {
        internal object Invocation { get; } = invocation;
        internal DbContextId ContextLease { get; } = contextLease;
        internal Guid? Tenant { get; } = tenant;
        internal RelationalConnectionIdentity Connection { get; } = connection;
        internal DbTransaction? PhysicalTransaction { get; set; }
        internal bool CommitStarted { get; set; }
        internal bool SettledNormally { get; set; }
    }

    private sealed class Receipt<T>(OriginalOwnerSettlement owner, Attempt attempt, T value,
        OriginalOwnerDisposition disposition) : IOriginalOwnerSettlement<T>
    {
        internal OriginalOwnerSettlement Owner { get; } = owner;
        internal Attempt Attempt { get; } = attempt;
        internal T Value { get; } = value;
        internal int Consumed;
        public OriginalOwnerDisposition Disposition { get; } = disposition;
    }

    private sealed class SingleAttemptStrategy(DbContext dbContext)
        : ExecutionStrategy(dbContext, maxRetryCount: 0, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => false;
    }
}
