using System.Data;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

/// <summary>
/// Standalone native lifecycle authority. Selected identity serialization precedes the original
/// primary transaction, host/lifecycle slot and metadata writes. Replica identity is backed by a
/// lifetime exclusive lock on the actual SQLite database, never inferred from host inventory.
/// No external key store is written and no secret payload enters this context.
/// </summary>
public sealed partial class ModerationRecognitionKeyLifecycleRepository(
    ExploreDbContext context, IPrivacyIdentityFenceAuthority identity, IPrivacyErasureAuthority erasure,
    ModerationRecognitionKeyLifecycleReplicaOwner replicaOwner, TimeProvider clock)
    : IModerationRecognitionKeyLifecycleRepository
{
    private IQueryable<T> Rows<T>() where T : class =>
        context.Set<T>().IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure).AsNoTracking();

    public async Task<ModerationRecognitionKeyLifecycle?> FindAsync(Guid trustDomainId, CancellationToken token)
    {
        var lifecycle = await Rows<ModerationRecognitionKeyLifecycle>().Include(row => row.Keys)
            .SingleOrDefaultAsync(row => row.Id == trustDomainId, token);
        lifecycle?.ValidatePersistedState();
        return lifecycle;
    }

    public Task<ModerationRecognitionKeyLifecycle> InitializeAsync(ModerationRecognitionKeyRing observed,
        Func<CancellationToken, Task> verifySelectedAtCommit, CancellationToken token) =>
        ExecuteObservedAsync(async ct =>
        {
            var host = await AcquireAsync(observed.TrustDomainId, ct);
            if (await Rows<ModerationRecognitionKeyLifecycle>().AnyAsync(row => row.Id == host.TrustDomainId, ct)
                || await Rows<ModerationRecognitionCaptureItem>().AnyAsync(row => row.TrustDomainId == host.TrustDomainId, ct)
                || await Rows<KeyObligation>().AnyAsync(row => row.TrustDomainId == host.TrustDomainId, ct)
                || observed.Keys.Length != 1 || observed.RingGeneration != 1)
                throw Unavailable();
            var floor = await VerifyRestoreFloorAsync(ct);
            var key = observed.Keys.Single();
            var lifecycle = ModerationRecognitionKeyLifecycle.Initialize(host.TrustDomainId, host.Id,
                key.KeyId, key.KeyGeneration, key.VerificationTag, observed.RingGeneration, clock.GetUtcNow(),
                floor.HighWaterSequence, floor.RetainedFloorSequence);
            ModerationRecognitionKeyLifecycleSnapshot.RequireMatching(lifecycle, observed, clock.GetUtcNow());
            context.Add(lifecycle);
            return lifecycle;
        }, verifySelectedAtCommit, token);

    public Task<ModerationRecognitionKeyLifecycle> StageAsync(
        ModerationRecognitionKeyRing observed, long expectedRevision,
        Func<CancellationToken, Task> verifySelectedAtCommit, CancellationToken token) =>
        ExecuteObservedAsync(async ct =>
        {
            var lifecycle = await AcquireLifecycleAsync(observed.TrustDomainId, expectedRevision, ct);
            // A v1 readable key is the candidate; existing active/readable material must remain exact.
            var existing = lifecycle.Keys.Where(row => row.State != ModerationRecognitionKeyState.Retired).ToArray();
            var additions = observed.Keys.Where(row => existing.All(old => old.KeyId != row.KeyId)).ToArray();
            if (additions.Length != 1 || additions[0].IsWriting || observed.Keys.Length != existing.Length + 1
                || existing.Any(old => !observed.Keys.Any(row => row.KeyId == old.KeyId
                    && row.KeyGeneration == old.KeyGeneration && row.VerificationTag == old.VerificationTag
                    && row.IsWriting == (old.State == ModerationRecognitionKeyState.Writing))))
                throw Unavailable();
            var candidate = additions[0];
            lifecycle.Stage(candidate.KeyId, candidate.KeyGeneration, candidate.VerificationTag,
                observed.RingGeneration, clock.GetUtcNow());
            ModerationRecognitionKeyLifecycleSnapshot.RequireMatching(lifecycle, observed, clock.GetUtcNow());
            return lifecycle;
        }, verifySelectedAtCommit, token);

    public Task<KeyReplica> AcknowledgeAsync(
        ModerationRecognitionKeyRing independentlyObserved, long expectedRevision,
        Func<CancellationToken, Task> verifySelectedAtCommit, CancellationToken token) =>
        ExecuteObservedAsync(async ct =>
        {
            var lifecycle = await AcquireLifecycleAsync(independentlyObserved.TrustDomainId, expectedRevision, ct);
            var now = clock.GetUtcNow();
            if (independentlyObserved.ResolvedAt < lifecycle.ChangedAt
                || independentlyObserved.ObservedAt < lifecycle.ChangedAt) throw Unavailable();
            ModerationRecognitionKeyLifecycleSnapshot.RequireMatching(lifecycle, independentlyObserved, now);
            var replica = KeyReplica.Acknowledge(Guid.CreateVersion7(), replicaOwner.Acquire(context), lifecycle,
                independentlyObserved.BindingRevision, independentlyObserved.Source, now, independentlyObserved.CurrentUntil);
            context.Add(replica);
            return replica;
        }, verifySelectedAtCommit, token);

    public Task<ModerationRecognitionKeyLifecycle> SwitchAsync(
        ModerationRecognitionKeyRing observed, string keyId, long expectedRevision,
        Func<CancellationToken, Task> verifySelectedAtCommit, CancellationToken token) =>
        ExecuteObservedAsync(async ct =>
        {
            var lifecycle = await AcquireLifecycleAsync(observed.TrustDomainId, expectedRevision, ct);
            ModerationRecognitionKeyLifecycleSnapshot.RequireMatching(lifecycle, observed, clock.GetUtcNow());
            var runtime = replicaOwner.Acquire(context);
            var acknowledgement = await AcknowledgementAsync(lifecycle, runtime, ct);
            if (acknowledgement is null || acknowledgement.BindingRevision != observed.BindingRevision
                || acknowledgement.Source != observed.Source)
                throw Unavailable();
            lifecycle.AuthorizeSwitch(keyId, checked(lifecycle.RingGeneration + 1),
                acknowledgement, runtime, clock.GetUtcNow());
            return lifecycle;
        }, verifySelectedAtCommit, token);

    public Task<ModerationRecognitionKeyLifecycle> RetireAsync(
        ModerationRecognitionKeyRing observed, string keyId, long expectedRevision,
        Func<CancellationToken, Task> verifySelectedAtCommit, CancellationToken token) =>
        ExecuteObservedAsync(async ct =>
        {
            var trustDomainId = observed.TrustDomainId;
            var lifecycle = await AcquireLifecycleAsync(trustDomainId, expectedRevision, ct);
            ModerationRecognitionKeyLifecycleSnapshot.RequireMatching(lifecycle, observed, clock.GetUtcNow());
            var runtime = replicaOwner.Acquire(context);
            var acknowledgement = await AcknowledgementAsync(lifecycle, runtime, ct);
            if (acknowledgement is null || acknowledgement.BindingRevision != observed.BindingRevision
                || acknowledgement.Source != observed.Source)
                throw Unavailable();
            var obligations = await Rows<KeyObligation>()
                .Where(row => row.TrustDomainId == trustDomainId && row.KeyId == keyId).ToListAsync(ct);
            var items = await Rows<ModerationRecognitionCaptureItem>()
                .Where(row => row.TrustDomainId == trustDomainId && row.KeyId == keyId).ToListAsync(ct);
            if (items.Any(item => !obligations.Any(row => row.Matches(item)))) throw Unavailable();
            var verifiedPurges = new HashSet<Guid>();
            foreach (var obligation in obligations)
            {
                obligation.ValidatePersistedState();
                if (obligation.MaterialPrunedAt is null)
                {
                    var pending = await Rows<ModerationRecognitionPending>().Include(row => row.Material)
                        .SingleOrDefaultAsync(row => row.IntentId == obligation.IntentId, ct);
                    if (pending is null || !pending.HasCompleteMaterial
                        || pending.State != ModerationRecognitionPendingState.AuthorityBound
                        || pending.Failure != ModerationRecognitionPendingFailure.None
                        || pending.AuthoritySequence != obligation.AuthoritySequence
                        || pending.MaterialReceipt != obligation.MaterialReceipt
                        || !pending.Material.Any(obligation.Matches))
                        throw Unavailable();
                }
                else if (verifiedPurges.Add(obligation.IntentId))
                {
                    var purge = await FindPurgeAsync(obligation.IntentId, ct);
                    if (purge is null || purge.AuthoritySequence != obligation.AuthoritySequence
                        || purge.TrustDomainId != trustDomainId || purge.BackupObligationUntil < obligation.ReadableUntil)
                        throw Unavailable();
                    await ReadPurgeObligationsAsync(purge, lifecycle, ct);
                }
            }
            var floor = await VerifyRestoreFloorAsync(ct, lifecycle);
            lifecycle.AuthorizeRetirement(keyId, checked(lifecycle.RingGeneration + 1), obligations,
                floor.HighWaterSequence, floor.HighWaterSequence, acknowledgement, runtime, clock.GetUtcNow());
            return lifecycle;
        }, verifySelectedAtCommit, token);

    public Task<ModerationRecognitionKeyLifecycle> CompromiseAsync(
        Guid trustDomainId, string keyId, long expectedRevision, CancellationToken token) =>
        ExecuteAsync(async ct =>
        {
            var lifecycle = await AcquireLifecycleAsync(trustDomainId, expectedRevision, ct);
            lifecycle.Compromise(keyId, clock.GetUtcNow());
            return lifecycle;
        }, token);

    public async Task ValidateAsync(ModerationRecognitionKeyRing ring, CancellationToken cancellationToken)
    {
        RequireTransaction();
        await AcquireAsync(ring.TrustDomainId, cancellationToken);
        var lifecycle = await Rows<ModerationRecognitionKeyLifecycle>().Include(row => row.Keys)
            .SingleOrDefaultAsync(row => row.Id == ring.TrustDomainId, cancellationToken) ?? throw Unavailable();
        ModerationRecognitionKeyLifecycleSnapshot.RequireMatching(lifecycle, ring, clock.GetUtcNow());
        // A restored application image can contain the right secret generation but still
        // predate an erasure. Matching/capture cannot turn that missing history into innocence.
        // Authority binding itself is keyless and intentionally does not require this join.
        await VerifyRestoreFloorAsync(cancellationToken, lifecycle);
    }

    public async Task EnlistCaptureAsync(ModerationRecognitionPending pending, CancellationToken token)
    {
        RequireTransaction();
        pending.ValidatePersistedState();
        if (!pending.HasCompleteMaterial) throw Unavailable();
        // Selected native adapters reject this read without their original live identity gate.
        // Its boolean is not enrollment permission, including after an authority fact is bound.
        _ = await identity.IsSubjectFencedAsync(pending.SubjectId, token);
        var trustDomain = pending.Material[0].TrustDomainId;
        await AcquireAsync(trustDomain, token);
        var lifecycle = context.Set<ModerationRecognitionKeyLifecycle>().Local.SingleOrDefault(row => row.Id == trustDomain)
            ?? await Rows<ModerationRecognitionKeyLifecycle>().Include(row => row.Keys)
                .SingleOrDefaultAsync(row => row.Id == trustDomain, token) ?? throw Unavailable();
        lifecycle.ValidatePersistedState();
        if (context.Entry(lifecycle).State == EntityState.Detached) context.Attach(lifecycle);
        foreach (var item in pending.Material)
        {
            var existing = context.Set<KeyObligation>().Local.SingleOrDefault(row => row.Id == item.Id)
                ?? await Rows<KeyObligation>().SingleOrDefaultAsync(row => row.Id == item.Id, token);
            if (existing is null)
            {
                // A new original or alias must use the current writer. Original settled obligations
                // can subsequently bind authority without changing their now-readable key identity.
                if (item.RingGeneration != lifecycle.RingGeneration
                    || !lifecycle.Keys.Any(row => row.State == ModerationRecognitionKeyState.Writing
                        && row.KeyId == item.KeyId && row.KeyGeneration == item.KeyGeneration
                        && row.VerificationTag == item.KeyVerificationTag))
                    throw Unavailable();
                var source = await Rows<ModerationRecognitionPrivacySource>().Include(row => row.Retention)
                    .SingleOrDefaultAsync(row => row.Id == item.SourceId, token) ?? throw Unavailable();
                existing = KeyObligation.Capture(item, source, pending.MaterialReceipt!);
                context.Add(existing);
            }
            else
            {
                existing.ValidatePersistedState();
                if (!existing.Matches(item) || existing.MaterialReceipt != pending.MaterialReceipt
                    || existing.MaterialPrunedAt is not null) throw Unavailable();
                if (context.Entry(existing).State == EntityState.Detached) context.Attach(existing);
            }
            if (pending.AuthoritySequence is { } sequence) existing.BindAuthority(sequence);
            lifecycle.RecordObligation(existing);
        }
    }

    private async Task<ModerationHostBinding> AcquireAsync(Guid trustDomainId, CancellationToken token)
    {
        RequireTransaction();
        _ = replicaOwner.Acquire(context);
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
        if (host is null || host.TrustDomainId != trustDomainId) throw Unavailable();
        // Local capability coverage is necessary, but is not used as a substitute for the process lock.
        if (await Rows<ModerationRecognitionPrivacySource>().AnyAsync(
                row => row.HostBindingId == host.Id && row.ModeCode != 1, token))
            throw new InvalidOperationException("moderation_recognition_topology_unavailable");
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationHostBinding>(context, host.Id, token);
        _ = await RelationalNamedLock.AcquireTransactionAsync(context,
            $"moderation.recognition-key-lifecycle:{trustDomainId:N}", token);
        return host;
    }

    private async Task<ModerationRecognitionKeyLifecycle> AcquireLifecycleAsync(
        Guid trustDomainId, long expectedRevision, CancellationToken token)
    {
        var host = await AcquireAsync(trustDomainId, token);
        var lifecycle = await Rows<ModerationRecognitionKeyLifecycle>().Include(row => row.Keys)
            .SingleOrDefaultAsync(row => row.Id == trustDomainId, token) ?? throw Unavailable();
        lifecycle.ValidatePersistedState();
        if (lifecycle.HostBindingId != host.Id || lifecycle.Revision != expectedRevision) throw Unavailable();
        context.Attach(lifecycle);
        return lifecycle;
    }

    private async Task<KeyReplica?> AcknowledgementAsync(
        ModerationRecognitionKeyLifecycle lifecycle, Guid runtime, CancellationToken token)
    {
        var acknowledgement = await Rows<KeyReplica>()
            .Where(row => row.TrustDomainId == lifecycle.Id && row.RuntimeReplicaId == runtime
                && row.RingGeneration == lifecycle.RingGeneration && row.RingCommitment == lifecycle.Commitment)
            .OrderByDescending(row => row.Sequence).FirstOrDefaultAsync(token);
        return acknowledgement?.Matches(lifecycle, runtime, clock.GetUtcNow()) == true ? acknowledgement : null;
    }

    private async Task<PrivacyErasureAuthorityState> VerifyRestoreFloorAsync(CancellationToken token,
        ModerationRecognitionKeyLifecycle? lifecycle = null)
    {
        var authority = await erasure.GetStateAsync(token);
        var checkpoint = await context.PrivacyErasureReplayCheckpoints.AsNoTracking()
            .OrderByDescending(row => row.AuthoritySequence).FirstOrDefaultAsync(token);
        if ((checkpoint?.AuthoritySequence ?? 0) != authority.HighWaterSequence
            || lifecycle is not null && (authority.HighWaterSequence < lifecycle.InitialErasureHead
                || authority.RetainedFloorSequence < lifecycle.InitialErasureRetainedFloor))
            throw new InvalidOperationException("moderation_recognition_restore_floor_unavailable");
        if (checkpoint is not null && authority.RetainedFloorSequence < checkpoint.AuthoritySequence)
        {
            var original = await erasure.ReadAfterAsync(checkpoint.AuthoritySequence - 1, 1, token);
            if (original.Count != 1 || !checkpoint.Matches(original[0]))
                throw new InvalidOperationException("moderation_recognition_restore_floor_unavailable");
        }
        return authority;
    }

    private Task<T> ExecuteObservedAsync<T>(Func<CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task> verifySelectedAtCommit, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(verifySelectedAtCommit);
        return ExecuteAsync(operation, verifySelectedAtCommit, token);
    }

    private Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken token) =>
        ExecuteAsync(operation, null, token);

    private Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task>? verifySelectedAtCommit, CancellationToken token) =>
        identity.ExecuteEnrollmentSerializedAsync(async ct =>
        {
            if (context.ChangeTracker.HasChanges() || context.IsTenantFilterBypassed) throw Unavailable();
            await using var transaction = context.Database.CurrentTransaction is null
                ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            if (transaction is null && !context.IdentityFenceOwnsTransaction) throw Unavailable();
            RequireTransaction();
            try
            {
                var result = await operation(ct);
                await context.SaveChangesAsync(ct);
                if (transaction is not null)
                {
                    if (verifySelectedAtCommit is not null) await verifySelectedAtCommit(ct);
                    await transaction.CommitAsync(ct);
                }
                return result;
            }
            finally { context.ChangeTracker.Clear(); }
        }, verifySelectedAtCommit ?? (_ => Task.CompletedTask), token);

    private void RequireTransaction()
    {
        if (context.Database.CurrentTransaction is not { } transaction
            || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable
            || transaction.GetDbTransaction().Connection != context.Database.GetDbConnection())
            throw Unavailable();
    }
    private static InvalidOperationException Unavailable() => new("moderation_recognition_lifecycle_unavailable");
}
