using System.Data;
using System.Runtime.ExceptionServices;
using Explore.Application.Services;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Identity;

/// <summary>
/// Keeps the selected retained identity authority outside native Local mutations.
/// A co-located authority already owns the primary transaction; a savepoint gives
/// rejected Local operations rollback without committing or disposing that authority.
/// </summary>
internal sealed class LocalIdentityMutationTransaction : IAsyncDisposable
{
    private readonly DbContext identity;
    private readonly ExploreDbContext application;
    private readonly IDbContextTransaction identityTransaction;
    private readonly IDbContextTransaction? applicationTransaction;
    private readonly bool ownsIdentityTransaction;
    private readonly string? savepoint;
    private readonly PrivacyIdentityFenceOperation identityFence;
    private readonly IAsyncDisposable? dispatchClaim;
    private bool completed;
    private bool nativeCommitDeferred;

    private LocalIdentityMutationTransaction(DbContext identity, ExploreDbContext application,
        IDbContextTransaction identityTransaction, IDbContextTransaction? applicationTransaction,
        bool ownsIdentityTransaction, string? savepoint, PrivacyIdentityFenceOperation identityFence,
        IAsyncDisposable? dispatchClaim)
    {
        this.identity = identity;
        this.application = application;
        this.identityTransaction = identityTransaction;
        this.applicationTransaction = applicationTransaction;
        this.ownsIdentityTransaction = ownsIdentityTransaction;
        this.savepoint = savepoint;
        this.identityFence = identityFence;
        this.dispatchClaim = dispatchClaim;
    }

    public static async Task<LocalIdentityMutationTransaction> BeginAsync(
        DbContext identity, ExploreDbContext application, PrivacyIdentityFenceOperation identityFence, CancellationToken token)
    {
        bool shared = ReferenceEquals(identity, application);
        bool adopted = application.IdentityFenceOwnsTransaction
            && application.Database.CurrentTransaction is not null;
        if (identity.Database.CurrentTransaction is not null && !(shared && adopted)
            || application.Database.CurrentTransaction is not null && !adopted)
            throw new InvalidOperationException("Local identity mutations cannot join an unrelated transaction.");

        IDbContextTransaction? native = null;
        IDbContextTransaction? primary = null;
        IAsyncDisposable? dispatchClaim = null;
        string? savepoint = adopted ? $"local_identity_{Guid.CreateVersion7():N}" : null;
        try
        {
            // Recognition and retained privacy coordination already surround this boundary.
            // Exclude PostgreSQL source writers before establishing our Serializable snapshot;
            // a trigger-time lock cannot protect earlier reads from a committed revision.
            // Adopted transactions retain their original owner's existing coordination.
            if (!adopted && application.Database.IsNpgsql())
                dispatchClaim = await RelationalNamedLock.AcquireSessionAsync(application,
                    EmailDispatchOutboxRepository.ClaimAdvisoryLockName, token).ConfigureAwait(false);
            native = shared && adopted
                ? application.Database.CurrentTransaction!
                : await identity.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            if (!shared && !adopted)
                primary = await application.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            if (savepoint is not null)
                await application.Database.CurrentTransaction!.CreateSavepointAsync(savepoint, token);
            return new(identity, application, native, primary, !(shared && adopted), savepoint, identityFence,
                dispatchClaim);
        }
        catch (Exception failure)
        {
            List<Exception> failures = [failure];
            foreach (IAsyncDisposable? resource in new IAsyncDisposable?[]
                { primary, shared && adopted ? null : native, dispatchClaim })
            {
                if (resource is null) continue;
                try { await resource.DisposeAsync().ConfigureAwait(false); }
                catch (Exception cleanupFailure) { failures.Add(cleanupFailure); }
            }
            if (failures.Count > 1)
                throw new AggregateException("Local mutation acquisition and cleanup failures.", failures);
            throw;
        }
    }

    internal void RequireOwner(DbContext originalIdentity, ExploreDbContext originalApplication)
    {
        if (completed || !ReferenceEquals(identity, originalIdentity) || !ReferenceEquals(application, originalApplication)
            || !ReferenceEquals(identity.Database.CurrentTransaction, identityTransaction))
            throw new UnauthorizedAccessException("Local mutation settlement requires its original selected context pair.");
    }

    public async Task CommitAsync(CancellationToken token, Action? requireCurrent = null)
    {
        requireCurrent?.Invoke();
        if (savepoint is null)
        {
            await application.FlushDisclosureAsync(token);
            await application.ValidateTrustedSetupSettingsSourcesAsync(token);
            await application.ValidateHumanCommitAdmissionsAsync(token);
            await identityFence.EnsureCurrentAsync(token);
            application.ValidateHumanCommitAdmissionTimes();
            application.ValidateTrustedSetupSettingsTimes();
            requireCurrent?.Invoke();
            if (applicationTransaction is not null)
                await applicationTransaction.CommitAsync(token);
        }
        if (savepoint is not null)
            await application.Database.CurrentTransaction!.ReleaseSavepointAsync(savepoint, token);
        if (ownsIdentityTransaction)
        {
            if (savepoint is not null)
            {
                // The external Local acknowledgement must not get ahead of the primary
                // mirror commit owned by the co-located retained authority.
                identityFence.DeferNativeIdentityCommit(identityTransaction, async () =>
                {
                    requireCurrent?.Invoke();
                    await identityTransaction.CommitAsync(token);
                    requireCurrent?.Invoke();
                });
                nativeCommitDeferred = true;
            }
            else
            {
                requireCurrent?.Invoke();
                await identityTransaction.CommitAsync(token);
            }
        }
        requireCurrent?.Invoke();
        completed = true;
    }

    public async Task RollbackAsync(CancellationToken token)
    {
        if (completed) return;
        if (savepoint is not null)
            await application.Database.CurrentTransaction!.RollbackToSavepointAsync(savepoint, token);
        if (applicationTransaction is not null)
            await applicationTransaction.RollbackAsync(token);
        if (ownsIdentityTransaction)
            await identityTransaction.RollbackAsync(token);
        application.ChangeTracker.Clear();
        application.ResetDisclosureMutations();
        completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        List<Exception> failures = [];
        try
        {
            if (!completed) await RollbackAsync(CancellationToken.None);
        }
        catch (Exception failure) { failures.Add(failure); }
        foreach (IAsyncDisposable? resource in new IAsyncDisposable?[]
            { applicationTransaction, ownsIdentityTransaction && !nativeCommitDeferred ? identityTransaction : null, dispatchClaim })
        {
            if (resource is null) continue;
            try { await resource.DisposeAsync().ConfigureAwait(false); }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1)
            throw new AggregateException("Local mutation settlement and cleanup failures.", failures);
    }
}
