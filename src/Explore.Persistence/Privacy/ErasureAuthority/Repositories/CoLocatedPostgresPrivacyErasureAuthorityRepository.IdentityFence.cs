using System.Data;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Privacy.ErasureAuthority.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Privacy.ErasureAuthority.Repositories;

public sealed partial class CoLocatedPostgresPrivacyErasureAuthorityRepository : IOriginalPrivacyIdentityFenceAuthority
{
    private readonly AsyncLocal<bool> _identityGate = new();
    private readonly AsyncLocal<PrivacyErasureRecoveryGate?> _recoveryGate = new();

    public Task<T> ExecuteEnrollmentSerializedAsync<T>(
        Func<CancellationToken, Task<T>> operation, Func<CancellationToken, Task> beforePrimaryCommit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(beforePrimaryCommit);
        if (_identityGate.Value)
            throw new InvalidOperationException("Nested enrollment must reuse its outer identity operation.");
        // PostgreSQL holds the retained authority transaction on its separate connection.
        // The primary owner, not this retained counter transaction, validates before commit.
        return ExecuteSerializedAsync(operation, cancellationToken);
    }

    public Task<T> ExecuteOriginalEnrollmentAsync<T>(
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        if (_identityGate.Value)
            throw new InvalidOperationException("Original enrollment cannot borrow another identity gate.");
        return ExecuteSerializedAsync(operation, cancellationToken);
    }

    public Task<T> ExecuteOriginalEnrollmentAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task> beforePrimaryCommit,
        Action<DateTimeOffset> requireCurrent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(beforePrimaryCommit);
        ArgumentNullException.ThrowIfNull(requireCurrent);
        if (_identityGate.Value || dbContext.Database.CurrentTransaction is not null
            || ExecutionStrategy.Current?.RetriesOnFailure == true)
            throw new InvalidOperationException("Original enrollment cannot borrow or replay another identity owner.");
        return ExecuteSerializedCoreAsync(operation, beforePrimaryCommit, requireCurrent, cancellationToken);
    }

    public async Task<T> ExecuteSerializedAsync<T>(
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        if (_identityGate.Value)
            return await operation(cancellationToken);
        return await ExecuteSerializedCoreAsync(operation, null, null, cancellationToken);
    }

    private async Task<T> ExecuteSerializedCoreAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task>? beforeCommit,
        Action<DateTimeOffset>? requireCurrent,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var contextLease = dbContext.ContextId;
        var physicalTransaction = transaction.GetDbTransaction();
        _ = await RelationalNamedLock.AcquireTransactionAsync(
            dbContext, "privacy-erasure-authority-counter", cancellationToken);
        dbContext.ChangeTracker.Clear();
        bool initialized = !await dbContext.AuthorityCounters.AnyAsync(cancellationToken);
        if (initialized)
        {
            if (await dbContext.ErasureIntents.AnyAsync(cancellationToken))
                throw new InvalidOperationException("privacy_identity_fence_authority_state_unavailable");
            dbContext.AuthorityCounters.Add(PrivacyErasureCounter.Start());
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        await dbContext.AuthorityCounters.ExecuteUpdateAsync(
            setters => setters.SetProperty(counter => counter.LastSequence, counter => counter.LastSequence),
            cancellationToken);
        PrivacyErasureCounter counter = await dbContext.AuthorityCounters.AsNoTracking().SingleAsync(cancellationToken);
        using var recoveryGate = new PrivacyErasureRecoveryGate(dbContext,
            initialized ? null : counter.GetRecoveryState());
        _identityGate.Value = true;
        _recoveryGate.Value = recoveryGate;
        try
        {
            T result = await operation(cancellationToken);
            RequireOriginalTransaction();
            if (beforeCommit is not null)
                await beforeCommit(cancellationToken);
            RequireOriginalTransaction();
            requireCurrent?.Invoke(timeProvider.GetUtcNow());
            RequireOriginalTransaction();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        finally
        {
            _identityGate.Value = false;
            _recoveryGate.Value = null;
            dbContext.ChangeTracker.Clear();
        }

        void RequireOriginalTransaction()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_identityGate.Value || dbContext.ContextId != contextLease
                || !ReferenceEquals(dbContext.Database.CurrentTransaction, transaction)
                || !ReferenceEquals(dbContext.Database.GetDbConnection(), physicalTransaction.Connection))
                throw new InvalidOperationException("Identity admission left its original live physical transaction.");
        }
    }

    public async Task<PrivacyErasureRecoveryState> AcquireRecoveryStateAsync(CancellationToken cancellationToken)
    {
        PrivacyErasureRecoveryGate gate = _recoveryGate.Value
            ?? throw new InvalidOperationException("privacy_erasure_recovery_gate_required");
        gate.RequireActive();
        RequireIdentityGate();
        if (dbContext.ChangeTracker.HasChanges())
            throw new InvalidOperationException("privacy_erasure_recovery_state_uncommitted");
        PrivacyErasureCounter counter = await dbContext.AuthorityCounters.AsNoTracking().SingleAsync(cancellationToken);
        return gate.Acquire(counter.GetRecoveryState());
    }

    public async Task ValidateKeyAsync(string keyId, string verificationTag, CancellationToken cancellationToken)
    {
        RequireIdentityGate();
        PrivacyErasureCounter counter = await dbContext.AuthorityCounters.SingleAsync(cancellationToken);
        if (counter.IdentityKeyId is null && await dbContext.Set<PrivacyErasureIdentityFence>().AnyAsync(cancellationToken))
            throw new InvalidOperationException("privacy_identity_fence_authority_state_unavailable");
        counter.BindIdentityKey(keyId, verificationTag);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<PrivacyErasureIntent?> FindAsync(
        PrivacyIdentityFingerprint fingerprint, CancellationToken cancellationToken)
    {
        RequireIdentityGate();
        return dbContext.ErasureIntents.AsNoTracking().Include(intent => intent.IdentityFences)
            .Where(intent => intent.IdentityFences.Any(fence =>
                fence.IdentityKind == fingerprint.IdentityKind && fence.KeyId == fingerprint.KeyId
                && fence.Fingerprint == fingerprint.Fingerprint))
            .OrderBy(intent => intent.AuthoritySequence).FirstOrDefaultAsync(cancellationToken);
    }

    private void RequireIdentityGate()
    {
        if (!_identityGate.Value)
            throw new InvalidOperationException("Identity lookup requires the authority gate.");
    }

    public Task<bool> IsSubjectFencedAsync(Guid userId, CancellationToken cancellationToken)
    {
        RequireIdentityGate();
        return dbContext.ErasureIntents.AsNoTracking().AnyAsync(
            intent => intent.SubjectKind == PrivacyErasureSubjectKind.User
                && intent.SubjectId == userId && !intent.IsLegalHoldPseudonymized, cancellationToken);
    }
}
