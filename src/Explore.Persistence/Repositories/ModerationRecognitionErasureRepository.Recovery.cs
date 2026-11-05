using System.Runtime.ExceptionServices;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationRecognitionErasureRepository
{
    public Task<bool> ReconcileRetainedAsync(PrivacyErasureIntent observed, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(observed);
        if (!ReferenceEquals(identity, erasure) || erasure is not IPrivacyErasureRecoveryAuthority recovery
            || ExecutionStrategy.Current?.RetriesOnFailure == true)
            throw new InvalidOperationException("moderation_recognition_recovery_owner_unavailable");
        Func<CancellationToken, Task>? verifyBorrowedOwner = null;
        return new CaptureRecoveryExecutionStrategy(context).ExecuteAsync(() =>
            identity.ExecuteEnrollmentSerializedAsync(async ct =>
            {
                var current = await erasure.GetStateAsync(ct);
                // Only a never-committed sequence permits a new, fully qualified append attempt.
                if (observed.AuthoritySequence > current.HighWaterSequence) return false;
                var committed = await recovery.AcquireRecoveryStateAsync(ct);
                await RequireRetainedFactAsync(observed, ct);
                var transaction = await BeginAsync(ct);
                Exception? failure = null;
                try
                {
                    var contextLease = context.ContextId;
                    var original = transaction ?? context.Database.CurrentTransaction!;
                    var physical = original.GetDbTransaction();
                    var connection = RelationalConnectionIdentity.Capture(context);
                    var backend = connection.BackendProcessId;
                    void RequireOriginalOwner()
                    {
                        if (context.ContextId != contextLease || !connection.Matches(context)
                            || connection.State != System.Data.ConnectionState.Open
                            || connection.BackendProcessId != backend || !connection.Owns(physical)
                            || !ReferenceEquals(context.Database.CurrentTransaction, original)
                            || !ReferenceEquals(original.GetDbTransaction(), physical))
                            throw new InvalidOperationException("moderation_recognition_recovery_owner_changed");
                    }
                    async Task RequireCurrentAsync(CancellationToken cancellation)
                    {
                        if (await recovery.AcquireRecoveryStateAsync(cancellation) != committed)
                            throw new InvalidOperationException("moderation_recognition_recovery_authority_changed");
                        RequireOriginalOwner();
                    }
                    if (transaction is null) verifyBorrowedOwner = RequireCurrentAsync;
                    RequireOriginalOwner();
                    await VerifyPurgeAsync(observed, ct);
                    await RequireCurrentAsync(ct);
                    // The retained fact predates this gate, so primary completion stays inside it.
                    await CommitAsync(transaction, ct);
                }
                catch (Exception primary)
                {
                    failure = primary;
                    if (transaction is null) context.IdentityFenceTransactionFailed = true;
                }
                finally
                {
                    context.ChangeTracker.Clear();
                    if (transaction is not null)
                    {
                        try { await transaction.DisposeAsync(); }
                        catch (Exception cleanup)
                        {
                            failure = failure is null ? cleanup : new AggregateException(failure, cleanup);
                        }
                    }
                }
                if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
                return true;
            }, ct => verifyBorrowedOwner?.Invoke(ct) ?? Task.CompletedTask, token));
    }

    private sealed class CaptureRecoveryExecutionStrategy(DbContext owner)
        : ExecutionStrategy(owner, maxRetryCount: 0, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => false;
    }
}
