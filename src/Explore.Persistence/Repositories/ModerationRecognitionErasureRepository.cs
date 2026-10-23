using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using ISLAMU.Moderation.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

/// <summary>
/// Purpose-qualified native erasure capture. The retained identity gate is owned by the
/// application coordinator; this repository owns the independent primary-store commit and
/// the exact source/subject/canonical row fences, without any administrator principal.
/// </summary>
public sealed partial class ModerationRecognitionErasureRepository(
    ExploreDbContext context, IModerationRecognitionKeyRingProvider keys, TimeProvider clock,
    IPrivacyIdentityFenceAuthority identity, IPrivacyErasureAuthority erasure,
    IModerationRecognitionKeyLifecycleRepository keyLifecycle)
    : IModerationRecognitionErasureRepository
{
    private const string RecognitionInstantCollation = "moderation_recognition_instant";
    private ExploreDbContext Context => context;
    private TimeProvider Clock => clock;
    private IModerationRecognitionKeyLifecycleRepository KeyLifecycle => keyLifecycle;
    private IQueryable<T> Rows<T>() where T : class =>
        context.Set<T>().IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure).AsNoTracking();

    public Task<ModerationRecognitionPending?> FindAsync(Guid intentId, CancellationToken token) =>
        Rows<ModerationRecognitionPending>().Include(row => row.Material)
            .SingleOrDefaultAsync(row => row.IntentId == intentId, token);

    public Task<ModerationRecognitionPending?> FindReceiptAsync(byte[] hash, CancellationToken token) =>
        Rows<ModerationRecognitionPending>().Include(row => row.Material)
            .SingleOrDefaultAsync(row => row.ReceiptHash == hash, token);

    public async Task<IReadOnlyList<ModerationRecognitionPending>> ReadReceivedAfterAsync(
        Guid? afterId, int limit, CancellationToken token)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        var dispositions = Rows<ModerationRecognitionDisposition>();
        return await Rows<ModerationRecognitionPending>()
            .Where(row => row.AuthoritySequence == null
                && !dispositions.Any(disposition =>
                    disposition.IntentId == row.IntentId && disposition.PendingRevision == row.Revision
                    && disposition.Revision == 3 && disposition.AuthoritySequence != null
                    && dispositions.Count(peer => peer.IntentId == row.IntentId
                        && peer.PendingRevision == row.Revision) == disposition.CoverageCount
                    && !dispositions.Any(peer => peer.IntentId == row.IntentId && peer.PendingRevision == row.Revision
                        && (peer.AuthoritySequence != disposition.AuthoritySequence || peer.Revision != 3)))
                && (afterId == null || row.IntentId.CompareTo(afterId.Value) > 0))
            .OrderBy(row => row.IntentId).Take(limit).ToListAsync(token);
    }

    public Task<int> ExpireMatchingEligibilityAsync(int limit, CancellationToken token)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        return identity.ExecuteSerializedAsync(async ct =>
        {
            await using var transaction = await BeginAsync(ct);
            try
            {
                var now = clock.GetUtcNow();
                var eligible = Rows<ModerationRecognitionCaptureItem>().Where(row => row.DroppedAt == null);
                IOrderedQueryable<ModerationRecognitionCaptureItem> ordered;
                if (context.Database.IsSqlite())
                {
                    // Preserve offsets and 100ns boundaries on the actual transaction
                    // connection. SQLite datetime/julianday would lose this precision.
                    RegisterRecognitionInstantCollation();
                    var instant = now.ToString("O", CultureInfo.InvariantCulture);
                    eligible = eligible.Where(row =>
                        EF.Functions.Collate((string)(object)row.RetainUntil, RecognitionInstantCollation).CompareTo(instant) <= 0
                        || row.EndsAt != null
                            && EF.Functions.Collate((string)(object)row.EndsAt, RecognitionInstantCollation).CompareTo(instant) <= 0);
                    ordered = eligible.OrderBy(row =>
                        EF.Functions.Collate((string)(object)row.RetainUntil, RecognitionInstantCollation)).ThenBy(row => row.Id);
                }
                else
                {
                    eligible = eligible.Where(row => row.RetainUntil <= now || row.EndsAt <= now);
                    ordered = eligible.OrderBy(row => row.RetainUntil).ThenBy(row => row.Id);
                }
                var candidates = await ordered.Take(limit).ToListAsync(ct);
                // Retained identity serialization precedes the same subject/receipt/item
                // anchors used by capture. No original provider input or ring is borrowed.
                foreach (var subject in candidates.SelectMany(row => new[] { row.SubjectId, row.ErasureSubjectId }).Distinct().Order())
                    await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, subject, ct);
                foreach (var intent in candidates.Select(row => row.IntentId).Distinct().Order())
                    await RelationalEntityRowFence.AcquireGlobalAsync<ModerationRecognitionPending>(context, intent, ct);
                foreach (var item in candidates.OrderBy(row => row.Id))
                    await RelationalEntityRowFence.AcquireGlobalAsync<ModerationRecognitionCaptureItem>(context, item.Id, ct);
                var ids = candidates.Select(row => row.Id).ToArray();
                var current = await eligible.Where(row => ids.Contains(row.Id)).ToListAsync(ct);
                foreach (var item in current)
                {
                    item.ValidatePersistedState();
                    context.Attach(item);
                    item.Drop(now);
                }
                await context.SaveChangesAsync(ct);
                await CommitAsync(transaction, ct);
                return current.Count;
            }
            finally { context.ChangeTracker.Clear(); }
        }, token);
    }

    public async Task RecordFailureAsync(PrivacyErasureRequest request,
        ModerationRecognitionPendingFailure failure, CancellationToken token)
    {
        // Only owned capture writes can be tracked here; the failed native transaction has
        // already rolled back. Never let those candidate rows masquerade as a durable receipt.
        context.ChangeTracker.Clear();
        await identity.ExecuteSerializedAsync(async ct =>
        {
            await using var transaction = await BeginAsync(ct);
            await AcquireSubjectAsync(request, ct);
            var pending = await ReadRequestAsync(request, ct);
            await FailAsync(pending, failure, ct);
            await CommitAsync(transaction, ct);
            context.ChangeTracker.Clear();
            return true;
        }, token);
    }

    public Task<ModerationRecognitionPending?> ReceiveAsync(PrivacyErasureRequest request,
        byte[] receiptHash, DateTimeOffset receiptExpiresAt, CancellationToken token) =>
        identity.ExecuteSerializedAsync(ct => ReceiveUnderIdentityAsync(request, receiptHash, receiptExpiresAt, ct), token);

    private async Task<ModerationRecognitionPending?> ReceiveUnderIdentityAsync(PrivacyErasureRequest request,
        byte[] receiptHash, DateTimeOffset receiptExpiresAt, CancellationToken token)
    {
        await using var transaction = await BeginAsync(token);
        var authority = await AcquireSubjectAsync(request, token);
        var existing = await ReadRequestAsync(request, token);
        if (existing is not null)
        {
            await CommitAsync(transaction, token);
            return existing;
        }
        var contributions = await ContributionsAsync(authority, request.SubjectId, token);
        if (contributions.Count == 0)
        {
            await CommitAsync(transaction, token);
            return null;
        }
        var now = clock.GetUtcNow();
        var earliest = contributions.Select(item => item.Source)
            .OrderBy(source => source.LawfulRequestDeadlineTicks).ThenBy(source => source.Id).First();
        var settleBy = contributions.Min(item => now.AddTicks(item.Source.LawfulRequestDeadlineTicks));
        var escalateAt = contributions.Min(item => now.AddTicks(item.Source.EscalationAfterTicks));
        var pending = ModerationRecognitionPending.Receive(request.IntentId, request.SubjectId, request.PolicyVersion,
            request.ReasonCode, earliest.PrivacyOwnerHumanId, earliest.RecoveryOwnerHumanId,
            now, settleBy, escalateAt, receiptHash, receiptExpiresAt);
        context.Set<ModerationRecognitionPending>().Add(pending);
        await context.SaveChangesAsync(token);
        await CommitAsync(transaction, token);
        context.ChangeTracker.Clear();
        return pending;
    }

    public async Task ComputeAsync(PrivacyErasureRequest request, CancellationToken token) =>
        await identity.ExecuteSerializedAsync(async ct =>
        {
            try
            {
                await ComputeUnderIdentityAsync(request, ct);
                return true;
            }
            catch
            {
                context.ChangeTracker.Clear();
                throw;
            }
        }, token);

    private async Task ComputeUnderIdentityAsync(PrivacyErasureRequest request, CancellationToken token)
    {
        await using var transaction = await BeginAsync(token);
        if (await HasConsumedLawfulDispositionAsync(request, token)
            || await HasRecoverableLawfulDispositionAsync(request, token)
            || await HasPrunedCaptureAsync(request, token)
            || await HasLawfulDispositionAsync(request, token))
        {
            // Exact legal authorization is bound to this pending revision. Computing or
            // advancing it here would invalidate that approval before the retained append.
            await CommitAsync(transaction, token);
            context.ChangeTracker.Clear();
            return;
        }
        var authority = await AcquireSubjectAsync(request, token);
        var pending = await ReadRequestAsync(request, token);
        if (pending is null)
        {
            await CommitAsync(transaction, token);
            return;
        }
        context.Attach(pending);
        var now = clock.GetUtcNow();
        pending.AdvanceDeadline(now);
        if (pending.MaterialReceipt is not null)
        {
            await context.SaveChangesAsync(token);
            await CommitAsync(transaction, token);
            context.ChangeTracker.Clear();
            return;
        }
        if (now >= pending.SettleBy)
        {
            pending.ObserveFailure(ModerationRecognitionPendingFailure.IncompleteMaterial, now);
            await context.SaveChangesAsync(token);
            await CommitAsync(transaction, token);
            context.ChangeTracker.Clear();
            return;
        }
        try
        {
            var contributions = await ContributionsAsync(authority, request.SubjectId, token);
            if (contributions.Count == 0)
            {
                // A fully reversed request needs no matching material, but no empty receipt is
                // manufactured. AcquireAppend independently proves the empty current inventory.
                await CommitAsync(transaction, token);
                return;
            }
            using var ring = await keys.ResolveAsync(authority.Host!.TrustDomainId, token);
            var writing = ring.Keys.Single(row => row.IsWriting);
            using var key = ring.BorrowWritingKey(ring.TrustDomainId, writing.KeyId, writing.KeyGeneration);
            var bindings = await ReadBindingsAsync(request.SubjectId, token);
            if (bindings.Count == 0) throw new CaptureUnavailable(ModerationRecognitionPendingFailure.IncompleteMaterial);
            var items = new List<ModerationRecognitionCaptureItem>();
            foreach (var contribution in contributions)
            {
                var matchingRetention = contribution.Source.Retention.Single(row =>
                    row.RecordClass == ModerationRecognitionPrivacyRecordClass.EligibleMatchingMaterial);
                var retainUntil = contribution.Parent?.RetainUntil
                    ?? pending.ReceivedAt.AddTicks(matchingRetention.MaximumRetentionTicks);
                if (contribution.Parent is null && retainUntil < pending.SettleBy || retainUntil <= now)
                    throw new CaptureUnavailable(ModerationRecognitionPendingFailure.SourceUnavailable);
                foreach (var purpose in Purposes(contribution))
                    foreach (var binding in bindings)
                    {
                        if (contribution.Parent is { } parent)
                        {
                            items.Add(ModerationRecognitionCaptureItem.Recapture(Guid.CreateVersion7(), request.IntentId,
                                request.SubjectId, parent, contribution.Binding!, contribution.Decision, contribution.Sanction,
                                key.Compute(binding, purpose), key.VerificationTag, ring.RingGeneration, now));
                            continue;
                        }
                        var material = new ModerationRecognitionCapturedMaterial(key.Compute(binding, purpose),
                            key.VerificationTag, ModerationRecognitionContribution.Capture(
                                contribution.Decision, contribution.Sanction, purpose, now, retainUntil));
                        items.Add(ModerationRecognitionCaptureItem.Capture(Guid.CreateVersion7(), request.IntentId,
                            contribution.Decision, material, contribution.Source.Id, contribution.Source.Revision,
                            contribution.Source.WriterGeneration, contribution.Adoption.Id, contribution.Adoption.Revision,
                            contribution.Adoption.WriterGeneration, ring.RingGeneration));
                    }
            }
            // Validate the original borrowed identity at the terminal time, not only at resolution.
            using var terminal = ring.BorrowWritingKey(ring.TrustDomainId, writing.KeyId, writing.KeyGeneration);
            // Pending.Capture validates the exhaustive single-writing-generation batch.
            // Recaptured rows keep the original ceiling even when it precedes this request's
            // lawful deadline; reaching that ceiling drops eligibility, never renews it.
            pending.Capture(items, now);
            await keyLifecycle.EnlistCaptureAsync(pending, token);
            await context.SaveChangesAsync(token);
            if (clock.GetUtcNow() >= pending.SettleBy || clock.GetUtcNow() >= ring.CurrentUntil
                || contributions.Any(item => clock.GetUtcNow() >= item.Source.ValidUntil
                    || clock.GetUtcNow() >= item.Adoption.ValidUntil))
                // Roll back the entire candidate capture. The previously committed Received
                // row survives; an expired flush must not become a complete durable receipt.
                throw new TimeoutException("moderation_recognition_capture_interval_expired");
        }
        catch (CaptureUnavailable unavailable)
        {
            pending.ObserveFailure(unavailable.Failure, clock.GetUtcNow());
            await context.SaveChangesAsync(token);
        }
        catch (InvalidOperationException exception) when (
            exception.Message.StartsWith("moderation_recognition_", StringComparison.Ordinal))
        {
            pending.ObserveFailure(ModerationRecognitionPendingFailure.KeyUnavailable, clock.GetUtcNow());
            await context.SaveChangesAsync(token);
        }
        await CommitAsync(transaction, token);
        context.ChangeTracker.Clear();
    }

    public async Task<IModerationRecognitionErasureLease> AcquireAppendAsync(
        PrivacyErasureRequest request, CancellationToken token)
    {
        var transaction = await BeginAsync(token);
        try
        {
            var lawful = await HasConsumedLawfulDispositionAsync(request, token)
                || await HasRecoverableLawfulDispositionAsync(request, token);
            if (!lawful && await HasPrunedCaptureAsync(request, token))
                return new AppendLease(this, context.Database.CurrentTransaction!, transaction,
                    request, null, canAppend: true, AppendKind.PrunedCapture);
            if (lawful || await HasLawfulDispositionAsync(request, token))
                return new AppendLease(this, context.Database.CurrentTransaction!, transaction,
                    request, null, canAppend: true, AppendKind.LawfulDisposition);
            var authority = await AcquireSubjectAsync(request, token);
            var pending = await ReadRequestAsync(request, token);
            var canAppend = await VerifyAsync(authority, request, pending, afterAuthority: false, token);
            return new AppendLease(this, context.Database.CurrentTransaction!, transaction, request, pending, canAppend,
                AppendKind.CapturedOrOrdinary);
        }
        catch
        {
            if (transaction is not null) await transaction.DisposeAsync();
            context.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task VerifyPurgeAsync(PrivacyErasureIntent fact, CancellationToken token)
    {
        RequireTransaction();
        var request = PrivacyErasureRequest.Create(fact.IntentId, fact.SubjectKind, fact.SubjectId, fact.ReasonCode, fact.PolicyVersion);
        var retained = (await erasure.ReadAfterAsync(fact.AuthoritySequence - 1, 1, token)).SingleOrDefault();
        if (retained is null || retained.AuthoritySequence != fact.AuthoritySequence
            || retained.IntentId != fact.IntentId || retained.SubjectKind != fact.SubjectKind
            || retained.SubjectId != fact.SubjectId || retained.ReasonCode != fact.ReasonCode
            || retained.PolicyVersion != fact.PolicyVersion || retained.RecordedAtUtc != fact.RecordedAtUtc
            || retained.RequestedAtUtc != fact.RequestedAtUtc || retained.IsLegalHoldPseudonymized)
            throw new InvalidOperationException("moderation_recognition_authority_unavailable");
        if (await keyLifecycle.AcquirePurgeAsync(fact, token) is not null) return;
        if (await VerifyLawfulDispositionAsync(fact, token)) return;
        if (await HasRecoverableLawfulDispositionAsync(request, token)
            || await HasLawfulDispositionAsync(request, token))
        {
            // Recover the exact retained append after a lost native consumption commit.
            // Unconsumed authorization still requires current qualified ownership.
            await BindLawfulDispositionAsync(fact, token);
            return;
        }
        var authority = await AcquireSubjectAsync(request, token);
        var pending = await ReadRequestAsync(request, token);
        if (!await VerifyAsync(authority, request, pending, afterAuthority: true, token))
            throw new InvalidOperationException("moderation_recognition_erasure_unsettled");
        if (pending is not null && pending.HasCompleteMaterial)
        {
            context.Attach(pending);
            pending.BindAuthority(fact, clock.GetUtcNow());
            await keyLifecycle.EnlistCaptureAsync(pending, token);
            await context.SaveChangesAsync(token);
        }
    }

    private async Task<bool> VerifyAsync(CaptureAuthority authority, PrivacyErasureRequest request,
        ModerationRecognitionPending? pending, bool afterAuthority, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        List<QualifiedContribution> contributions;
        try { contributions = await ContributionsAsync(authority, request.SubjectId, token); }
        catch (CaptureUnavailable unavailable)
        {
            return await FailAsync(pending, unavailable.Failure, token);
        }
        if (pending is null) return contributions.Count == 0;
        if (!pending.HasCompleteMaterial)
            return contributions.Count == 0 && pending.MaterialReceipt is null
                || await FailAsync(pending, ModerationRecognitionPendingFailure.IncompleteMaterial, token);
        foreach (var item in pending.Material) item.ValidatePersistedState();
        if (!afterAuthority && now >= pending.SettleBy)
            return await FailAsync(pending, ModerationRecognitionPendingFailure.IncompleteMaterial, token);
        context.Attach(pending);
        foreach (var item in pending.Material.Where(row => row.DroppedAt is null))
        {
            if (item.ErasureSubjectId != request.SubjectId)
                return await FailAsync(pending, ModerationRecognitionPendingFailure.OriginalMaterialChanged, token);
            var canonical = await CanonicalAsync(item.SubjectId, item.DecisionId, item.SanctionId, token);
            if (canonical is null) return await FailAsync(pending, ModerationRecognitionPendingFailure.SourceUnavailable, token);
            if (!item.MatchesCurrent(canonical.Value.Decision, canonical.Value.Sanction, now)
                || !await HasFinalFindingAsync(canonical.Value.Decision, token))
            {
                item.Drop(now);
                continue;
            }
            var current = contributions.SingleOrDefault(row => row.Sanction.Id == item.SanctionId);
            if (item.ParentCaptureItemId is not null)
            {
                try
                {
                    if (!await RecaptureLineageCurrentAsync(item, token) || current is null)
                    {
                        item.Drop(now);
                        continue;
                    }
                }
                catch (ContinuityUnavailable)
                {
                    return await FailAsync(pending, ModerationRecognitionPendingFailure.SourceUnavailable, token);
                }
                if (current.Parent?.Id != item.ParentCaptureItemId || current.Binding?.Id != item.ContinuityBindingId
                    || !item.PreservesOriginalContribution(current.Parent))
                    return await FailAsync(pending, ModerationRecognitionPendingFailure.OriginalMaterialChanged, token);
            }
            if (current is null || !Purposes(current).Contains(item.Purpose)
                || current.Source.Id != item.SourceId || current.Source.Revision != item.SourceRevision
                || current.Source.WriterGeneration != item.SourceGeneration || current.Adoption.Id != item.AdoptionId
                || current.Adoption.Revision != item.AdoptionRevision
                || current.Adoption.WriterGeneration != item.AdoptionGeneration)
                return await FailAsync(pending, ModerationRecognitionPendingFailure.SourceUnavailable, token);
        }
        if (contributions.Any(contribution => Purposes(contribution).Any(purpose =>
            !pending.Material.Any(row => row.SanctionId == contribution.Sanction.Id && row.Purpose == purpose
                && row.DroppedAt is null && row.MatchesCurrent(contribution.Decision, contribution.Sanction, now)))))
            return await FailAsync(pending, ModerationRecognitionPendingFailure.IncompleteMaterial, token);
        var surviving = pending.Material.Where(row => row.DroppedAt is null).ToArray();
        // Before append, independently recompute every original binding under the readable
        // original key. After the retained fact exists, the complete pre-fact receipt and
        // current canonical contributions are the recovery authority: neither destroyed
        // provider inputs nor a now-unavailable secret may be prerequisites for reconciliation.
        // Matching and readable-generation retirement remain separately closed native gates.
        if (surviving.Length != 0 && !afterAuthority)
        {
            try
            {
                using var ring = await keys.ResolveAsync(authority.Host!.TrustDomainId, token);
                var bindings = await ReadBindingsAsync(request.SubjectId, token);
                foreach (var item in surviving)
                {
                    using var key = ring.BorrowReadableKey(item.TrustDomainId, item.KeyId, item.KeyGeneration);
                    if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(key.VerificationTag),
                            Convert.FromHexString(item.KeyVerificationTag)))
                        return await FailAsync(pending, ModerationRecognitionPendingFailure.OriginalMaterialChanged, token);
                    if (!bindings.Any(binding => key.Compute(binding, item.Purpose) == item.Token))
                        return await FailAsync(pending, ModerationRecognitionPendingFailure.IncompleteMaterial, token);
                }
                // New native provider bindings cannot be omitted between compute and append.
                foreach (var contribution in contributions)
                    foreach (var purpose in Purposes(contribution))
                        foreach (var binding in bindings)
                        {
                            var original = surviving.First(row => row.SanctionId == contribution.Sanction.Id && row.Purpose == purpose);
                            using var key = ring.BorrowReadableKey(original.TrustDomainId, original.KeyId, original.KeyGeneration);
                            var computed = key.Compute(binding, purpose);
                            if (!surviving.Any(row => row.SanctionId == contribution.Sanction.Id && row.Token == computed))
                                return await FailAsync(pending, ModerationRecognitionPendingFailure.IncompleteMaterial, token);
                        }
            }
            catch (InvalidOperationException exception) when (
                exception.Message.StartsWith("moderation_recognition_", StringComparison.Ordinal))
            {
                return await FailAsync(pending, ModerationRecognitionPendingFailure.KeyUnavailable, token);
            }
        }
        await context.SaveChangesAsync(token);
        return true;
    }

    private async Task<bool> FailAsync(ModerationRecognitionPending? pending,
        ModerationRecognitionPendingFailure failure, CancellationToken token)
    {
        if (pending is not null && pending.AuthoritySequence is null)
        {
            if (context.Entry(pending).State == EntityState.Detached) context.Attach(pending);
            pending.ObserveFailure(failure, clock.GetUtcNow());
            await context.SaveChangesAsync(token);
        }
        return false;
    }

    private async Task<ModerationRecognitionPending?> ReadRequestAsync(PrivacyErasureRequest request, CancellationToken token)
    {
        var existing = await Rows<ModerationRecognitionPending>().Include(row => row.Material)
            .SingleOrDefaultAsync(row => row.SubjectId == request.SubjectId || row.IntentId == request.IntentId, token);
        existing?.ValidatePersistedState();
        if (existing is not null && (existing.IntentId != request.IntentId || existing.SubjectId != request.SubjectId
            || existing.PolicyVersion != request.PolicyVersion || existing.ReasonCode != request.ReasonCode))
            throw new ConcurrencyConflictException("privacy_erasure_request_conflict", "A different privacy request already owns this subject.");
        return existing;
    }

    private async Task<IReadOnlyList<ProviderAccountKey>> ReadBindingsAsync(Guid subjectId, CancellationToken token)
    {
        var logins = await Rows<UserExternalLogin>().Where(row => row.UserId == subjectId).OrderBy(row => row.Id).ToListAsync(token);
        foreach (var row in logins)
            await RelationalEntityRowFence.AcquireGlobalAsync<UserExternalLogin>(context, row.Id, token);
        // These are already qualified native provider keys, not provider locators, email observations,
        // DID handles, user profile fields or an invented issuer/subject normalization.
        return logins.Where(row => row.AuthenticationProviderId != (int)AuthenticationProviderKind.Local)
            .Select(row => new ProviderAccountKey((AuthenticationProviderKind)row.AuthenticationProviderId,
            row.ProviderKey)).Distinct().ToArray();
    }

    private async Task<IDbContextTransaction?> BeginAsync(CancellationToken token)
    {
        if (context.ChangeTracker.HasChanges() || context.IsTenantFilterBypassed)
            throw new InvalidOperationException("Recognition cannot borrow uncommitted or bypassed caller state.");
        if (context.Database.CurrentTransaction is not null)
        {
            if (!context.IdentityFenceOwnsTransaction) throw new InvalidOperationException("Recognition requires its own native transaction.");
            RequireTransaction();
            return null;
        }
        context.ChangeTracker.Clear();
        return await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
    }

    private void RequireTransaction()
    {
        if (context.Database.CurrentTransaction is not { } transaction
            || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable
            || transaction.GetDbTransaction().Connection != context.Database.GetDbConnection())
            throw new InvalidOperationException("Recognition requires the original serializable native connection.");
    }

    private static Task CommitAsync(IDbContextTransaction? transaction, CancellationToken token) =>
        transaction is null ? Task.CompletedTask : transaction.CommitAsync(token);

    private void RegisterRecognitionInstantCollation() =>
        ((SqliteConnection)context.Database.GetDbConnection()).CreateCollation(RecognitionInstantCollation,
            static (left, right) => DateTimeOffset.Parse(left, CultureInfo.InvariantCulture)
                .CompareTo(DateTimeOffset.Parse(right, CultureInfo.InvariantCulture)));

    private async Task<bool> HasPrunedCaptureAsync(PrivacyErasureRequest request, CancellationToken token)
    {
        var purge = await keyLifecycle.FindPurgeAsync(request.IntentId, token);
        if (purge is null) return false;
        var fact = (await erasure.ReadAfterAsync(purge.AuthoritySequence - 1, 1, token)).SingleOrDefault();
        if (fact is null || fact.IntentId != request.IntentId || fact.SubjectId != request.SubjectId
            || fact.SubjectKind != request.SubjectKind || fact.PolicyVersion != request.PolicyVersion
            || fact.ReasonCode != request.ReasonCode || fact.IsLegalHoldPseudonymized
            || await keyLifecycle.AcquirePurgeAsync(fact, token) is null)
            throw new InvalidOperationException("moderation_recognition_pruned_proof_unavailable");
        return true;
    }

    private enum AppendKind { CapturedOrOrdinary, LawfulDisposition, PrunedCapture }

    private sealed class AppendLease(ModerationRecognitionErasureRepository owner, IDbContextTransaction original,
        IDbContextTransaction? owned, PrivacyErasureRequest request, ModerationRecognitionPending? pending, bool canAppend,
        AppendKind kind)
        : IModerationRecognitionErasureLease
    {
        private bool disposed;
        private DateTimeOffset? prunedDeadline;
        public bool CanAppend => !disposed && canAppend;
        public async Task BindAsync(PrivacyErasureIntent fact, CancellationToken token)
        {
            if (!CanAppend || !ReferenceEquals(owner.Context.Database.CurrentTransaction, original)
                || fact.SubjectId != request.SubjectId || fact.IntentId != request.IntentId
                || fact.PolicyVersion != request.PolicyVersion || fact.ReasonCode != request.ReasonCode)
                throw new InvalidOperationException("Recognition append lost its original native request and transaction.");
            if (kind == AppendKind.LawfulDisposition)
            {
                await owner.BindLawfulDispositionAsync(fact, token);
                return;
            }
            if (kind == AppendKind.PrunedCapture)
            {
                var purge = await owner.KeyLifecycle.AcquirePurgeAsync(fact, token)
                    ?? throw new InvalidOperationException("moderation_recognition_pruned_proof_unavailable");
                prunedDeadline = purge.RetainUntil;
                return;
            }
            var authority = await owner.AcquireSubjectAsync(request, token);
            if (!await owner.VerifyAsync(authority, request, pending, afterAuthority: false, token))
                throw new InvalidOperationException("moderation_recognition_erasure_unsettled");
            if (pending?.HasCompleteMaterial == true)
            {
                pending.BindAuthority(fact, owner.Clock.GetUtcNow());
                await owner.KeyLifecycle.EnlistCaptureAsync(pending, token);
                await owner.Context.SaveChangesAsync(token);
            }
            else if (pending is not null)
            {
                owner.Context.Attach(pending);
                pending.BindNoLongerRequired(fact, owner.Clock.GetUtcNow());
                await owner.Context.SaveChangesAsync(token);
            }
        }
        public Task CommitAsync(CancellationToken token)
        {
            if (disposed) throw new ObjectDisposedException(nameof(AppendLease));
            if (kind == AppendKind.PrunedCapture && prunedDeadline is { } until && owner.Clock.GetUtcNow() >= until)
                throw new InvalidOperationException("moderation_recognition_pruned_proof_expired");
            return owned is null ? Task.CompletedTask : owned.CommitAsync(token);
        }
        public async ValueTask DisposeAsync()
        {
            if (disposed) return;
            disposed = true;
            if (owned is not null) await owned.DisposeAsync();
            owner.Context.ChangeTracker.Clear();
        }
    }

    private sealed class CaptureUnavailable(ModerationRecognitionPendingFailure failure) : Exception
    {
        public ModerationRecognitionPendingFailure Failure { get; } = failure;
    }
}
