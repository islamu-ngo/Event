using System.Data;
using Explore.Domain;
using Explore.Persistence.Privacy.ErasureAuthority.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Privacy.ErasureAuthority.Repositories;

public sealed partial class EmbeddedPrivacyErasureAuthorityRepository
{
    private readonly AsyncLocal<EmbeddedPrivacyErasureAuthorityDbContext?> _identityGate = new();

    public async Task<T> ExecuteSerializedAsync<T>(
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        if (_identityGate.Value is not null)
            return await operation(cancellationToken);

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
            _ = await ReadOrInitializeIdentityCounterAsync(db, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            _identityGate.Value = db;
            T result = await operation(cancellationToken);
            if (applicationContext?.IdentityFenceTransactionFailed == true)
                throw new InvalidOperationException("The identity enrollment transaction has failed.");
            if (applicationEnlistment is not null)
            {
                if (applicationContext!.StorageReferenceTransactionFailed)
                    throw new InvalidOperationException("The storage reference transaction has failed.");
                await applicationContext.FlushDisclosureAsync(cancellationToken);
            }
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
            if (applicationEnlistment is not null)
            {
                applicationContext!.IdentityFenceOwnsTransaction = false;
                applicationContext.IdentityFenceTransactionFailed = false;
                await applicationEnlistment.DisposeAsync();
            }
        }
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
