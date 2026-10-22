using System.Data;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Identity;

/// <summary>Invalidates current Local eligibility without reading or changing credential material.</summary>
internal sealed class LocalIdentityEligibilityRevocation(DbContext identityContext, ExploreDbContext applicationContext,
    IPrivacyErasureAuthority authority, TimeProvider timeProvider) : ILocalIdentityEligibilityRevocation, IAsyncDisposable
{
    private readonly DbContext identityDb = identityContext;
    private LocalIdentityLifecycleGateCustody? custody;
    private PrivacyErasureIntent? originalIntent;
    private Lease? lease;
    private int disposed;

    public async Task<IAsyncDisposable> AcquireAsync(PrivacyErasureIntent intent, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(intent);
        if (custody is not null || applicationContext.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Local eligibility revocation requires its original pre-BEGIN owner.");
        applicationContext.RequireBoundTenantLease();
        var original = LocalIdentityLifecycleGateCustody.Create(identityDb);
        custody = original;
        try
        {
            await original.AcquireAsync(cancellationToken).ConfigureAwait(false);
            var retained = await authority.ReadAfterAsync(intent.AuthoritySequence - 1, 1, cancellationToken).ConfigureAwait(false);
            original.RequireCurrent();
            if (retained.Count != 1 || !SameIntent(retained[0], intent))
                throw new UnauthorizedAccessException("Local eligibility revocation requires the exact retained erasure fact.");
            originalIntent = intent;
            if (!ReferenceEquals(identityDb, applicationContext))
            {
                // Separate stores cannot commit atomically. Invalidation is deliberately durable first;
                // an interrupted application cleanup remains denied and is resumed by retained replay.
                await using var transaction = await identityDb.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
                await InvalidateAsync(intent, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            lease = new Lease(this, original);
            return lease;
        }
        catch
        {
            originalIntent = null;
            try { await original.DisposeAsync().ConfigureAwait(false); }
            finally { if (ReferenceEquals(custody, original)) custody = null; }
            throw;
        }
    }

    public async Task InvalidateInCurrentTransactionAsync(PrivacyErasureIntent intent, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref disposed) != 0 || lease is null || !ReferenceEquals(originalIntent, intent))
            throw new UnauthorizedAccessException("Erasure cannot borrow a different Local revocation scope.");
        custody!.RequireCurrent();
        if (applicationContext.Database.CurrentTransaction is null)
            throw new UnauthorizedAccessException("Local invalidation must precede binding deletion in the original application transaction.");
        if (ReferenceEquals(identityDb, applicationContext))
            await InvalidateAsync(intent, cancellationToken).ConfigureAwait(false);
        else if (!intent.IsLegalHoldPseudonymized)
        {
            bool stillEligible = await identityDb.Set<LocalIdentityEligibility>().AsNoTracking()
                .AnyAsync(row => row.LocalSubjectId == intent.SubjectId && row.State != LocalIdentityEligibilityState.Deleted,
                    cancellationToken).ConfigureAwait(false);
            if (stillEligible) throw new UnauthorizedAccessException("The separate identity invalidation is not durable.");
        }
        custody.RequireCurrent();
    }

    private async Task InvalidateAsync(PrivacyErasureIntent intent, CancellationToken token)
    {
        custody!.RequireCurrent();
        if (!ReferenceEquals(originalIntent, intent) || identityDb.Database.CurrentTransaction is null)
            throw new UnauthorizedAccessException("Local invalidation left its original identity transaction.");
        if (intent.IsLegalHoldPseudonymized) return;
        var row = await identityDb.Set<LocalIdentityEligibility>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.LocalSubjectId == intent.SubjectId, token).ConfigureAwait(false);
        if (row is null || row.State == LocalIdentityEligibilityState.Deleted) return;
        long generation = row.Generation;
        Guid operation = row.CurrentOperationId;
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        if (now < intent.RecordedAtUtc) now = intent.RecordedAtUtc;
        if (now < (row.UpdatedAt ?? row.CreatedAt)) now = row.UpdatedAt ?? row.CreatedAt;
        row.Delete(generation, operation, now);
        custody.RequireCurrent();
        int changed = await identityDb.Set<LocalIdentityEligibility>()
            .Where(value => value.Id == row.Id && value.LocalSubjectId == intent.SubjectId
                && value.CurrentOperationId == operation && value.Generation == generation)
            .ExecuteUpdateAsync(update => update.SetProperty(value => value.State, row.State)
                .SetProperty(value => value.Generation, row.Generation).SetProperty(value => value.UpdatedAt, row.UpdatedAt), token)
            .ConfigureAwait(false);
        if (changed != 1) throw new UnauthorizedAccessException("The Local eligibility image changed before erasure.");
        custody.RequireCurrent();
    }

    private static bool SameIntent(PrivacyErasureIntent retained, PrivacyErasureIntent supplied) =>
        retained.IntentId == supplied.IntentId && retained.AuthoritySequence == supplied.AuthoritySequence
        && retained.SubjectKind == supplied.SubjectKind && retained.SubjectId == supplied.SubjectId
        && retained.ReasonCode == supplied.ReasonCode && retained.PolicyVersion == supplied.PolicyVersion
        && retained.RequestedAtUtc == supplied.RequestedAtUtc && retained.RecordedAtUtc == supplied.RecordedAtUtc
        && retained.RetentionExpiresAtUtc == supplied.RetentionExpiresAtUtc
        && retained.IsLegalHoldPseudonymized == supplied.IsLegalHoldPseudonymized
        && retained.HasSameIdentityFences(supplied.IdentityFences.Select(value => value.GetFingerprint()));

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        if (lease is { } active) await active.DisposeAsync().ConfigureAwait(false);
        else if (custody is { } pending) await pending.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class Lease(LocalIdentityEligibilityRevocation owner, LocalIdentityLifecycleGateCustody original) : IAsyncDisposable
    {
        private int released;
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref released, 1) != 0) return;
            try { await original.DisposeAsync().ConfigureAwait(false); }
            finally
            {
                owner.originalIntent = null;
                if (ReferenceEquals(owner.lease, this)) owner.lease = null;
                if (ReferenceEquals(owner.custody, original)) owner.custody = null;
            }
        }
    }
}
