using System.Collections.Immutable;
using System.Data;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationRecognitionKeyLifecycleRepository
{
    public async Task<ModerationRecognitionKeyLifecyclePurge?> FindPurgeAsync(Guid intentId, CancellationToken token)
    {
        var purge = await Rows<ModerationRecognitionKeyLifecyclePurge>()
            .SingleOrDefaultAsync(row => row.IntentId == intentId, token);
        purge?.ValidatePersistedState();
        var now = clock.GetUtcNow();
        return purge is not null && now >= purge.PurgedAt && now < purge.RetainUntil ? purge : null;
    }

    public async Task<ModerationRecognitionKeyLifecyclePurge?> AcquirePurgeAsync(
        PrivacyErasureIntent fact, CancellationToken token)
    {
        RequireTransaction();
        if (fact.SubjectKind != PrivacyErasureSubjectKind.User || fact.IsLegalHoldPseudonymized) return null;
        // Discovery cannot retain late host/key locks on the ordinary capture path, which
        // still has to acquire sorted User/canonical anchors. A candidate is not authority.
        var candidate = await FindPurgeAsync(fact.IntentId, token);
        if (candidate is null || candidate.AuthoritySequence != fact.AuthoritySequence) return null;
        _ = await identity.IsSubjectFencedAsync(fact.SubjectId, token);
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token) ?? throw Unavailable();
        if (candidate.TrustDomainId != host.TrustDomainId) throw Unavailable();
        await AcquireAsync(host.TrustDomainId, token);
        var purge = await FindPurgeAsync(fact.IntentId, token);
        if (purge is null || purge.TrustDomainId != host.TrustDomainId || purge.AuthoritySequence != fact.AuthoritySequence)
            throw Unavailable();
        var lifecycle = await FindAsync(host.TrustDomainId, token) ?? throw Unavailable();
        await ReadPurgeObligationsAsync(purge, lifecycle, token);
        var authority = await VerifyRestoreFloorAsync(token, lifecycle);
        if (fact.AuthoritySequence <= authority.RetainedFloorSequence
            || fact.AuthoritySequence > authority.HighWaterSequence) throw Unavailable();
        var actual = (await erasure.ReadAfterAsync(fact.AuthoritySequence - 1, 1, token)).SingleOrDefault();
        if (actual is null || actual.IntentId != fact.IntentId || actual.AuthoritySequence != fact.AuthoritySequence
            || actual.SubjectKind != fact.SubjectKind || actual.SubjectId != fact.SubjectId
            || actual.PolicyVersion != fact.PolicyVersion || actual.ReasonCode != fact.ReasonCode
            || actual.RequestedAtUtc != fact.RequestedAtUtc || actual.RecordedAtUtc != fact.RecordedAtUtc
            || actual.RetentionExpiresAtUtc != fact.RetentionExpiresAtUtc || actual.IsLegalHoldPseudonymized
            || !actual.HasSameIdentityFences(fact.IdentityFences.Select(row => row.GetFingerprint())))
            throw Unavailable();
        if (clock.GetUtcNow() >= purge.RetainUntil) throw Unavailable();
        return purge;
    }

    public Task<ModerationRecognitionKeyLifecycleCleanup> PruneAsync(
        Guid? afterIntentId, int limit, CancellationToken token)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        return ExecuteAsync(async ct =>
        {
            // Cursor bounds work even when an early set remains live or has surviving descendants.
            // Comparisons on UTC instants happen after this bounded native page, preserving SQLite ticks.
            var sets = await Rows<ModerationRecognitionPending>().Include(row => row.Material)
                .Where(row => row.AuthoritySequence != null
                    && (afterIntentId == null || row.IntentId.CompareTo(afterIntentId.Value) > 0))
                .OrderBy(row => row.IntentId).Take(limit).ToListAsync(ct);
            if (sets.Count == 0)
                return new ModerationRecognitionKeyLifecycleCleanup(null, []);
            var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(ct) ?? throw Unavailable();
            await AcquireAsync(host.TrustDomainId, ct);
            var floor = await VerifyRestoreFloorAsync(ct);
            var now = clock.GetUtcNow();
            var result = new List<ModerationRecognitionKeyLifecyclePurge>();
            foreach (var pending in sets)
            {
                pending.ValidatePersistedState();
                if (!pending.HasCompleteMaterial || pending.State != ModerationRecognitionPendingState.AuthorityBound
                    || pending.Failure != ModerationRecognitionPendingFailure.None
                    || pending.AuthoritySequence > floor.HighWaterSequence
                    || pending.Material.Any(row => row.TrustDomainId != host.TrustDomainId
                        || (row.DroppedAt is null || row.DroppedAt > now)
                            && now < row.RetainUntil && (row.EndsAt is null || now < row.EndsAt)))
                    continue;
                var ids = pending.Material.Select(row => row.Id).ToArray();
                // Recapture ancestry is immutable. An ancestor cannot disappear while another set
                // still references its receipt; a subsequent page/pass removes newly released leaves.
                if (await Rows<ModerationRecognitionCaptureItem>().AnyAsync(row =>
                    row.ParentCaptureItemId != null && ids.Contains(row.ParentCaptureItemId.Value)
                    && row.IntentId != pending.IntentId, ct)) continue;
                var bindings = await Rows<ModerationRecognitionContinuityBinding>()
                    .Where(row => ids.Contains(row.CaptureItemId)).ToListAsync(ct);
                var bindingIds = bindings.Select(row => row.Id).ToArray();
                if (await Rows<ModerationRecognitionCaptureItem>().AnyAsync(row =>
                        row.ContinuityBindingId != null && bindingIds.Contains(row.ContinuityBindingId.Value)
                        && row.IntentId != pending.IntentId, ct)) continue;
                var recovery = await Rows<ModerationRecognitionActionRecovery>()
                    .Where(row => ids.Contains(row.CaptureItemId)).ToListAsync(ct);
                var obligations = await Rows<KeyObligation>().Where(row => row.IntentId == pending.IntentId).ToListAsync(ct);
                if (obligations.Count != pending.MaterialCount
                    || obligations.Any(row => !pending.Material.Any(row.Matches)
                        || row.MaterialReceipt != pending.MaterialReceipt
                        || row.AuthoritySequence != pending.AuthoritySequence))
                    throw Unavailable();
                foreach (var obligation in obligations)
                {
                    context.Attach(obligation);
                    obligation.Prune(pending.Material.Single(row => row.Id == obligation.Id), now);
                }
                var purge = ModerationRecognitionKeyLifecyclePurge.Complete(pending, obligations, now);
                context.Add(purge);
                context.RemoveRange(recovery);
                context.RemoveRange(bindings);
                context.RemoveRange(pending.Material);
                context.Remove(pending);
                result.Add(purge);
            }
            return new ModerationRecognitionKeyLifecycleCleanup(sets.LastOrDefault()?.IntentId,
                result.ToImmutableArray());
        }, token);
    }

    public async Task<ModerationRecognitionKeyLifecycleReceiptCleanup> ForgetExpiredReceiptsAsync(
        Guid? afterIntentId, int limit, CancellationToken token)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges()
            || context.IsTenantFilterBypassed) throw Unavailable();
        var candidates = await Rows<ModerationRecognitionKeyLifecyclePurge>()
            .Where(row => afterIntentId == null || row.IntentId.CompareTo(afterIntentId.Value) > 0)
            .OrderBy(row => row.IntentId).Select(row => row.IntentId).Take(limit).ToArrayAsync(token);
        if (candidates.Length == 0) return new(null, 0, 0);
        // Expiring an already-purged proof cannot change any subject, capture, eligibility or
        // authority fact. It needs no external authority call; a provider outage cannot renew it.
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            var receipts = await Rows<ModerationRecognitionKeyLifecyclePurge>()
                .Where(row => candidates.Contains(row.IntentId)).OrderBy(row => row.IntentId).ToListAsync(token);
            if (receipts.Count == 0)
            {
                await transaction.CommitAsync(token);
                return new(candidates[^1], 0, 0);
            }
            var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token) ?? throw Unavailable();
            await AcquireAsync(host.TrustDomainId, token);
            var lifecycle = await FindAsync(host.TrustDomainId, token) ?? throw Unavailable();
            var removed = 0;
            var removedObligations = 0;
            foreach (var receipt in receipts)
            {
                receipt.ValidatePersistedState();
                if (receipt.TrustDomainId != host.TrustDomainId) throw Unavailable();
                if (clock.GetUtcNow() < receipt.RetainUntil) continue;
                var obligations = await ReadPurgeObligationsAsync(receipt, lifecycle, token);
                context.RemoveRange(obligations);
                context.Remove(receipt);
                removed++;
                removedObligations += obligations.Count;
            }
            await context.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return new(candidates[^1], removed, removedObligations);
        }
        finally { context.ChangeTracker.Clear(); }
    }

    private async Task<List<KeyObligation>> ReadPurgeObligationsAsync(
        ModerationRecognitionKeyLifecyclePurge purge, ModerationRecognitionKeyLifecycle lifecycle, CancellationToken token)
    {
        purge.ValidatePersistedState();
        lifecycle.ValidatePersistedState();
        var obligations = await Rows<KeyObligation>().Where(row => row.IntentId == purge.IntentId).ToListAsync(token);
        if (purge.TrustDomainId != lifecycle.Id || obligations.Count != purge.MaterialCount
            || obligations.Count == 0
            || await Rows<ModerationRecognitionPending>().AnyAsync(row => row.IntentId == purge.IntentId, token)
            || await Rows<ModerationRecognitionCaptureItem>().AnyAsync(row => row.IntentId == purge.IntentId, token))
            throw Unavailable();
        foreach (var obligation in obligations)
        {
            obligation.ValidatePersistedState();
            if (obligation.TrustDomainId != purge.TrustDomainId || obligation.MaterialReceipt != purge.MaterialReceipt
                || obligation.AuthoritySequence != purge.AuthoritySequence || obligation.MaterialPrunedAt != purge.PurgedAt
                || obligation.ReceiptRetainUntil is null
                || !lifecycle.Keys.Any(key => key.KeyId == obligation.KeyId && key.KeyGeneration == obligation.KeyGeneration
                    && key.VerificationTag == obligation.VerificationTag
                    && key.OriginalObligationsUntil >= obligation.ReadableUntil
                    && key.RequiredErasureHead >= obligation.AuthoritySequence))
                throw Unavailable();
        }
        if (obligations.Max(row => row.ReadableUntil) != purge.BackupObligationUntil
            || obligations.Min(row => row.ReceiptRetainUntil!.Value) != purge.RetainUntil)
            throw Unavailable();
        return obligations;
    }
}
