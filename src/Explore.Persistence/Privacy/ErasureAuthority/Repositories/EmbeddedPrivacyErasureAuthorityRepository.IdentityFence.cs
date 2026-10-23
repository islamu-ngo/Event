using System.Data;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;
using Explore.Persistence.Privacy.ErasureAuthority.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Privacy.ErasureAuthority.Repositories;

public sealed partial class EmbeddedPrivacyErasureAuthorityRepository : IOriginalPrivacyIdentityFenceAuthority
{
    private readonly AsyncLocal<EmbeddedPrivacyErasureAuthorityDbContext?> _identityGate = new();
    private readonly AsyncLocal<PrivacyErasureRecoveryGate?> _recoveryGate = new();

    internal bool UsesOriginalApplicationContext(ExploreDbContext originalOwner) =>
        ReferenceEquals(applicationContext, originalOwner) && originalOwner.Database.IsSqlite();

    public Task<T> ExecuteSerializedAsync<T>(
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
        _identityGate.Value is not null
            ? operation(cancellationToken)
            : ExecuteEnrollmentSerializedAsync(operation, _ => Task.CompletedTask, cancellationToken);

    public Task<T> ExecuteOriginalEnrollmentAsync<T>(
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        if (_identityGate.Value is not null)
            throw new InvalidOperationException("Original enrollment cannot borrow another identity gate.");
        return ExecuteSerializedAsync(operation, cancellationToken);
    }

    public Task<T> ExecuteOriginalEnrollmentAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task> beforePrimaryCommit,
        Action<DateTimeOffset> requireCurrent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requireCurrent);
        return ExecuteEnrollmentSerializedCoreAsync(operation, beforePrimaryCommit, requireCurrent, cancellationToken);
    }

    internal async Task<T> ExecuteJoinedOriginalEnrollmentAsync<T>(ExploreDbContext originalOwner,
        Func<CancellationToken, Task<T>> settleOriginalOwner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settleOriginalOwner);
        if (!ReferenceEquals(applicationContext, originalOwner) || _identityGate.Value is not null
            || originalOwner.IdentityFenceOwnsTransaction || !originalOwner.HumanCommitAttemptOwnsTransaction
            || !originalOwner.HasHumanCommitContextLease || originalOwner.IsTenantFilterBypassed
            || originalOwner.Database.CurrentTransaction is not { } transaction
            || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Joined identity authority requires the original human Serializable owner.");

        await EnsureStorageReadyAsync(cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!EmbeddedIdentityFenceConnection.TryShareApplicationConnection(originalOwner, db, joinOriginalTransaction: true))
            throw new InvalidOperationException("Joined identity authority requires the original co-located SQLite store.");
        await using var enlistment = await db.Database.UseTransactionAsync(transaction.GetDbTransaction(), cancellationToken);
        PrivacyErasureCounter counter = await ReadOrInitializeIdentityCounterAsync(db, cancellationToken);
        using var recoveryGate = new PrivacyErasureRecoveryGate(db,
            db.Entry(counter).State == EntityState.Added ? null : counter.GetRecoveryState());
        await db.SaveChangesAsync(cancellationToken);
        _identityGate.Value = db;
        _recoveryGate.Value = recoveryGate;
        try
        {
            // Only the original owner commits or rolls back this shared physical transaction.
            return await settleOriginalOwner(cancellationToken);
        }
        finally
        {
            _identityGate.Value = null;
            _recoveryGate.Value = null;
        }
    }

    public Task<T> ExecuteEnrollmentSerializedAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task> beforePrimaryCommit,
        CancellationToken cancellationToken) =>
        ExecuteEnrollmentSerializedCoreAsync(operation, beforePrimaryCommit, null, cancellationToken);

    private async Task<T> ExecuteEnrollmentSerializedCoreAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task> beforePrimaryCommit,
        Action<DateTimeOffset>? requireCurrent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(beforePrimaryCommit);
        if (_identityGate.Value is not null)
            throw new InvalidOperationException("Nested enrollment must reuse its outer identity operation.");

        await EnsureStorageReadyAsync(cancellationToken);
        await using EmbeddedPrivacyErasureAuthorityDbContext db =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        bool colocated = EmbeddedIdentityFenceConnection.TryShareApplicationConnection(applicationContext, db);
        // Microsoft.Data.Sqlite starts a non-deferred write transaction here. Its
        // database lock, not WriterLock, orders writers in other processes.
        await using IDbContextTransaction transaction =
            await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        IDbContextTransaction? applicationEnlistment = null;
        try
        {
            if (colocated)
            {
                applicationEnlistment = await applicationContext!.Database.UseTransactionAsync(
                    transaction.GetDbTransaction(), cancellationToken);
                applicationContext.IdentityFenceOwnsTransaction = true;
                applicationContext.IdentityFenceTransactionFailed = false;
            }
            PrivacyErasureCounter counter = await ReadOrInitializeIdentityCounterAsync(db, cancellationToken);
            using var recoveryGate = new PrivacyErasureRecoveryGate(db,
                db.Entry(counter).State == EntityState.Added ? null : counter.GetRecoveryState());
            await db.SaveChangesAsync(cancellationToken);
            _identityGate.Value = db;
            _recoveryGate.Value = recoveryGate;
            T result = await operation(cancellationToken);
            if (applicationContext?.IdentityFenceTransactionFailed == true)
                throw new InvalidOperationException("The identity enrollment transaction has failed.");
            if (applicationEnlistment is not null)
            {
                if (applicationContext!.StorageReferenceTransactionFailed)
                    throw new InvalidOperationException("The storage reference transaction has failed.");
                await applicationContext.FlushDisclosureAsync(cancellationToken);
                await applicationContext.ValidateTrustedSetupSettingsSourcesAsync(cancellationToken);
                await applicationContext.ValidateHumanCommitAdmissionsAsync(cancellationToken);
                await beforePrimaryCommit(cancellationToken);
                applicationContext.ValidateHumanCommitAdmissionTimes();
                applicationContext.ValidateTrustedSetupSettingsTimes();
            }
            else if (requireCurrent is not null)
            {
                await beforePrimaryCommit(cancellationToken);
            }
            requireCurrent?.Invoke(timeProvider.GetUtcNow());
            await transaction.CommitAsync(cancellationToken);
            storage?.HardenCompanionFiles();
            return result;
        }
        catch
        {
            if (applicationEnlistment is not null)
            {
                applicationContext!.ChangeTracker.Clear();
                applicationContext.ResetDisclosureMutations();
            }
            throw;
        }
        finally
        {
            _identityGate.Value = null;
            _recoveryGate.Value = null;
            if (applicationEnlistment is not null)
            {
                applicationContext!.IdentityFenceOwnsTransaction = false;
                applicationContext.IdentityFenceTransactionFailed = false;
                applicationContext.ResetHumanCommitAdmissions();
                applicationContext.ResetTrustedSetupSettingsAdmissions();
                await applicationEnlistment.DisposeAsync();
            }
        }
    }

    public async Task<PrivacyErasureRecoveryState> AcquireRecoveryStateAsync(CancellationToken cancellationToken)
    {
        PrivacyErasureRecoveryGate gate = _recoveryGate.Value
            ?? throw new InvalidOperationException("privacy_erasure_recovery_gate_required");
        gate.RequireActive();
        EmbeddedPrivacyErasureAuthorityDbContext db = _identityGate.Value!;
        if (db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("privacy_erasure_recovery_state_uncommitted");
        PrivacyErasureCounter counter = await db.AuthorityCounters.AsNoTracking().SingleAsync(cancellationToken);
        return gate.Acquire(counter.GetRecoveryState());
    }

    public async Task ValidateKeyAsync(string keyId, string verificationTag, CancellationToken cancellationToken)
    {
        EmbeddedPrivacyErasureAuthorityDbContext db = _identityGate.Value
            ?? throw new InvalidOperationException("Identity key validation requires the authority gate.");
        PrivacyErasureCounter counter = await ReadOrInitializeIdentityCounterAsync(db, cancellationToken);
        counter.BindIdentityKey(keyId, verificationTag);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<PrivacyErasureCounter> ReadOrInitializeIdentityCounterAsync(
        EmbeddedPrivacyErasureAuthorityDbContext db, CancellationToken cancellationToken)
    {
        PrivacyErasureCounter? counter = await db.AuthorityCounters.SingleOrDefaultAsync(cancellationToken);
        if (counter is null)
        {
            if (await db.ErasureIntents.AnyAsync(cancellationToken))
                throw new InvalidOperationException("privacy_identity_fence_authority_state_unavailable");
            counter = PrivacyErasureCounter.Start();
            db.AuthorityCounters.Add(counter);
        }
        if (counter.IdentityKeyId is null && await db.Set<PrivacyErasureIdentityFence>().AnyAsync(cancellationToken))
            throw new InvalidOperationException("privacy_identity_fence_authority_state_unavailable");
        return counter;
    }

    public Task<PrivacyErasureIntent?> FindAsync(
        PrivacyIdentityFingerprint fingerprint, CancellationToken cancellationToken)
    {
        EmbeddedPrivacyErasureAuthorityDbContext db = _identityGate.Value
            ?? throw new InvalidOperationException("Identity lookup requires the authority gate.");
        return db.ErasureIntents.AsNoTracking().Include(intent => intent.IdentityFences)
            .Where(intent => intent.IdentityFences.Any(fence =>
                fence.IdentityKind == fingerprint.IdentityKind && fence.KeyId == fingerprint.KeyId
                && fence.Fingerprint == fingerprint.Fingerprint))
            .OrderBy(intent => intent.AuthoritySequence).FirstOrDefaultAsync(cancellationToken);
    }

    public Task<bool> IsSubjectFencedAsync(Guid userId, CancellationToken cancellationToken)
    {
        EmbeddedPrivacyErasureAuthorityDbContext db = _identityGate.Value
            ?? throw new InvalidOperationException("Subject lookup requires the authority gate.");
        return db.ErasureIntents.AsNoTracking().AnyAsync(
            intent => intent.SubjectKind == PrivacyErasureSubjectKind.User
                && intent.SubjectId == userId && !intent.IsLegalHoldPseudonymized, cancellationToken);
    }
}
