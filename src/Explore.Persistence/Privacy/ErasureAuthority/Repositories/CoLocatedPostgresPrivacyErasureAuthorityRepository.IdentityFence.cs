using System.Data;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Privacy.ErasureAuthority.Repositories;

public sealed partial class CoLocatedPostgresPrivacyErasureAuthorityRepository
{
    private readonly AsyncLocal<bool> _identityGate = new();

    public async Task<T> ExecuteSerializedAsync<T>(
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        if (_identityGate.Value)
            return await operation(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        _ = await RelationalNamedLock.AcquireTransactionAsync(
            dbContext, "privacy-erasure-authority-counter", cancellationToken);
        dbContext.ChangeTracker.Clear();
        if (!await dbContext.AuthorityCounters.AnyAsync(cancellationToken))
        {
            if (await dbContext.ErasureIntents.AnyAsync(cancellationToken))
                throw new InvalidOperationException("privacy_identity_fence_authority_state_unavailable");
            dbContext.AuthorityCounters.Add(PrivacyErasureCounter.Start());
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        await dbContext.AuthorityCounters.ExecuteUpdateAsync(
            setters => setters.SetProperty(counter => counter.LastSequence, counter => counter.LastSequence),
            cancellationToken);
        _identityGate.Value = true;
        try
        {
            T result = await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        finally
        {
            _identityGate.Value = false;
            dbContext.ChangeTracker.Clear();
        }
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
