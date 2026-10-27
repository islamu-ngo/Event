using System.Collections.Immutable;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationRecognitionErasureRepository : IModerationRecognitionContinuityRepository
{
    public async Task<ModerationRecognitionContinuityEvaluation> MatchAndBindAsync(
        ModerationRecognitionContinuityRequest request, CancellationToken token)
    {
        try { return await MatchAndBindCoreAsync(request, includeAdmission: true, token); }
        catch (ContinuityUnavailable failure) { return ModerationRecognitionContinuityEvaluation.Closed(failure.Failure); }
        catch (Exception exception) when (!token.IsCancellationRequested && exception is DbException or IOException or TimeoutException)
        {
            return ModerationRecognitionContinuityEvaluation.Closed(ModerationRecognitionContinuityFailure.StoreUnavailable);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return ModerationRecognitionContinuityEvaluation.Closed(ModerationRecognitionContinuityFailure.CanonicalUnavailable);
        }
    }

    private async Task<ModerationRecognitionContinuityEvaluation> MatchAndBindCoreAsync(
        ModerationRecognitionContinuityRequest request, bool includeAdmission, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Account);
        await RequireContinuityBoundaryAsync(request.FreshSubjectId, token);
        await RequireSettledSubjectAsync(request.FreshSubjectId, token);
        // A new native credential UUID says nothing about an erased person's other identities.
        if (request.Account.ProviderKind == AuthenticationProviderKind.Local) return new();

        // This lookup ONLY closes unreceipted old-subject requests. It never supplies a match:
        // that requires the computed, purpose-separated HMAC below. Recheck ordinal equality
        // because a provider database collation may be broader than ProviderAccountKey.
        var liveBindings = await Rows<UserExternalLogin>().Where(row =>
                row.AuthenticationProviderId == (int)request.Account.ProviderKind
                && row.ProviderKey == request.Account.Value)
            .Select(row => new { row.UserId, row.ProviderKey }).ToListAsync(token);
        foreach (var binding in liveBindings.Where(row => string.Equals(
            row.ProviderKey, request.Account.Value, StringComparison.Ordinal)))
            await RequireSettledSubjectAsync(binding.UserId, token);

        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
        if (host is null)
        {
            if (await Rows<ModerationRecognitionCaptureItem>().AnyAsync(token))
                throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.SourceUnavailable);
            return new();
        }
        var jurisdiction = await ReadEnrollmentJurisdictionAsync(host, request.EnrollmentTenantId, token);
        var now = clock.GetUtcNow();
        var eligible = Rows<ModerationRecognitionCaptureItem>().Where(row =>
                row.TrustDomainId == host.TrustDomainId && row.ProviderKind == request.Account.ProviderKind
                && row.DroppedAt == null
                && (includeAdmission || row.Purpose == ModerationReferenceRecognition.SeparatelyQualifiedFinalAction));
        if (context.Database.IsSqlite())
        {
            RegisterRecognitionInstantCollation();
            var instant = now.ToString("O", CultureInfo.InvariantCulture);
            eligible = eligible.Where(row =>
                EF.Functions.Collate((string)(object)row.RetainUntil, RecognitionInstantCollation).CompareTo(instant) > 0
                && (row.EndsAt == null
                    || EF.Functions.Collate((string)(object)row.EndsAt, RecognitionInstantCollation).CompareTo(instant) > 0));
        }
        else
            eligible = eligible.Where(row => row.RetainUntil > now && (row.EndsAt == null || row.EndsAt > now));
        var inventory = await eligible.ToListAsync(token);
        inventory = inventory.Where(row =>
            row.Purpose != ModerationReferenceRecognition.SeparatelyQualifiedFinalEnrollment
            || ModerationRecognitionContinuityBinding.CoversAdmission(row, jurisdiction, now)).ToList();
        if (inventory.Count == 0) return await CompleteActionRecoveryAsync(request.FreshSubjectId,
            request.VerifiedBindingId, new(), token);

        ModerationRecognitionKeyRing ring;
        try { ring = await keys.ResolveAsync(host.TrustDomainId, token); }
        catch (Exception exception) when (!token.IsCancellationRequested
            && exception is InvalidOperationException or IOException or TimeoutException)
        {
            return await StageActionRecoveryAsync(request.FreshSubjectId, request.VerifiedBindingId, inventory,
                PurposeFailure(inventory, ModerationRecognitionContinuityFailure.KeyUnavailable), token);
        }
        using (ring)
        {
            var matched = new List<ModerationRecognitionCaptureItem>();
            var unresolved = new List<ModerationRecognitionCaptureItem>();
            var result = new ModerationRecognitionContinuityEvaluation { CurrentUntil = ring.CurrentUntil };
            // Actual readable generations, not metadata promises or only the writing generation.
            foreach (var group in inventory.GroupBy(row => (row.Purpose, row.KeyId, row.KeyGeneration)))
            {
                try
                {
                    using var key = ring.BorrowReadableKey(host.TrustDomainId, group.Key.KeyId, group.Key.KeyGeneration);
                    foreach (var item in group)
                    {
                        item.ValidatePersistedState();
                        if (ring.RingGeneration < item.RingGeneration || item.SerializationVersion != ring.SerializationVersion
                            || !SameKey(key.VerificationTag, item.KeyVerificationTag))
                            throw new InvalidOperationException("moderation_recognition_original_key_unavailable");
                    }
                    var computed = key.Compute(request.Account, group.Key.Purpose);
                    var candidates = await Rows<ModerationRecognitionCaptureItem>().Where(row =>
                        row.TrustDomainId == computed.TrustDomainId && row.ProviderKind == computed.ProviderKind
                        && row.Purpose == computed.Purpose && row.KeyId == computed.KeyId
                        && row.KeyGeneration == computed.KeyGeneration && row.SerializationVersion == computed.SerializationVersion
                        && row.Digest == computed.Digest).ToListAsync(token);
                    matched.AddRange(candidates.Where(row => row.Token == computed
                        && group.Any(original => original.Id == row.Id)));
                }
                catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
                {
                    result = WithPurposeFailure(result, group.Key.Purpose, ModerationRecognitionContinuityFailure.KeyUnavailable);
                    unresolved.AddRange(group);
                }
            }
            if (matched.Count == 0)
            {
                var evaluated = await StageActionRecoveryAsync(request.FreshSubjectId,
                    request.VerifiedBindingId, unresolved, result, token);
                return await CompleteActionRecoveryAsync(request.FreshSubjectId, request.VerifiedBindingId, evaluated, token);
            }
            CaptureAuthority authority;
            try
            {
                var subjects = await ReadContinuityLineageSubjectsAsync(matched, token);
                authority = await AcquireContinuityAuthorityAsync(host,
                    subjects.Append(request.FreshSubjectId), request.EnrollmentTenantId, token);
            }
            catch (ContinuityUnavailable failure)
            {
                foreach (var purpose in matched.Select(row => row.Purpose).Distinct())
                    result = WithPurposeFailure(result, purpose, failure.Failure);
                return await StageActionRecoveryAsync(request.FreshSubjectId, request.VerifiedBindingId,
                    unresolved.Concat(matched).DistinctBy(item => item.Id).ToArray(), result, token);
            }
            var actions = new List<QualifiedContinuity>();
            foreach (var item in matched.OrderBy(row => row.DecisionId).ThenBy(row => row.SanctionId).ThenBy(row => row.Id))
            {
                try
                {
                    var qualified = await QualifyContinuityAsync(authority, item, token);
                    if (qualified is null) continue;
                    result = result with { CurrentUntil = Earlier(result.CurrentUntil, qualified.CurrentUntil) };
                    if (item.Purpose == ModerationReferenceRecognition.SeparatelyQualifiedFinalAction)
                        actions.Add(qualified);
                    else if (ModerationRecognitionContinuityBinding.CoversAdmission(item, jurisdiction, clock.GetUtcNow()))
                        result = result with { AdmissionExcluded = true };
                }
                catch (ContinuityUnavailable failure)
                {
                    result = WithPurposeFailure(result, item.Purpose, failure.Failure);
                    unresolved.Add(item);
                }
            }
            if (result.AdmissionExcluded || result.AdmissionFailure != ModerationRecognitionContinuityFailure.None
                || result.ActionFailure != ModerationRecognitionContinuityFailure.None)
                return await StageActionRecoveryAsync(request.FreshSubjectId, request.VerifiedBindingId,
                    unresolved.DistinctBy(item => item.Id).ToArray(), result, token);
            var bindings = new List<ModerationRecognitionContinuityBinding>();
            var selected = new List<ModerationRecognitionCaptureItem>();
            foreach (var group in actions.GroupBy(row => row.Item.SanctionId))
            {
                var current = group.First();
                var existing = context.ChangeTracker.Entries<ModerationRecognitionContinuityBinding>()
                    .Where(entry => entry.State == EntityState.Added).Select(entry => entry.Entity)
                    .SingleOrDefault(row => row.FreshSubjectId == request.FreshSubjectId && row.SanctionId == group.Key)
                    ?? await Rows<ModerationRecognitionContinuityBinding>().SingleOrDefaultAsync(row =>
                        row.FreshSubjectId == request.FreshSubjectId && row.SanctionId == group.Key, token);
                if (existing is not null)
                {
                    existing.ValidatePersistedState();
                    var original = await Rows<ModerationRecognitionCaptureItem>()
                        .SingleOrDefaultAsync(row => row.Id == existing.CaptureItemId, token)
                        ?? throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.CanonicalUnavailable);
                    if (existing.CaptureMaterialReceipt != original.ComputeMaterialReceipt())
                        throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.CanonicalUnavailable);
                    QualifiedContinuity? requalified;
                    try
                    {
                        RequireReadableContinuityKey(ring, original);
                        requalified = await QualifyContinuityAsync(authority, original, token);
                    }
                    catch (ContinuityUnavailable failure)
                    {
                        return await StageActionRecoveryAsync(request.FreshSubjectId, request.VerifiedBindingId,
                            [original], result with { ActionFailure = failure.Failure }, token);
                    }
                    // Never rebind a stale reference to a different item or a replacement decision.
                    if (requalified is null || !existing.IsCurrent(original, requalified.Decision, requalified.Sanction, clock.GetUtcNow()))
                        continue;
                    current = requalified;
                }
                var bound = existing ?? ModerationRecognitionContinuityBinding.Bind(Guid.CreateVersion7(),
                    request.FreshSubjectId, current.Item, current.Decision, current.Sanction, clock.GetUtcNow());
                bindings.Add(bound);
                selected.Add(current.Item);
                result = result with { CurrentUntil = Earlier(result.CurrentUntil, current.CurrentUntil) };
            }
            if (clock.GetUtcNow() >= result.CurrentUntil)
                return await StageActionRecoveryAsync(request.FreshSubjectId, request.VerifiedBindingId, inventory,
                    PurposeFailure(inventory, ModerationRecognitionContinuityFailure.SourceUnavailable), token);
            foreach (var binding in bindings)
                if (!await Rows<ModerationRecognitionContinuityBinding>().AnyAsync(row => row.Id == binding.Id, token)
                    && context.Entry(binding).State == EntityState.Detached)
                    context.Set<ModerationRecognitionContinuityBinding>().Add(binding);
            return await CompleteActionRecoveryAsync(request.FreshSubjectId, request.VerifiedBindingId,
                result with { Bindings = bindings.ToImmutableArray(), CurrentActions = selected.ToImmutableArray() }, token);
        }
    }

    public async Task<ModerationRecognitionContinuityFailure> GuardExistingSubjectAsync(Guid subjectId, CancellationToken token)
    {
        try
        {
            await RequireContinuityBoundaryAsync(subjectId, token);
            await RequireSettledSubjectAsync(subjectId, token);
            return ModerationRecognitionContinuityFailure.None;
        }
        catch (ContinuityUnavailable failure) { return failure.Failure; }
        catch (Exception exception) when (!token.IsCancellationRequested && exception is DbException or IOException or TimeoutException)
        {
            return ModerationRecognitionContinuityFailure.StoreUnavailable;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return ModerationRecognitionContinuityFailure.CanonicalUnavailable;
        }
    }

    private async Task RequireContinuityBoundaryAsync(Guid subjectId, CancellationToken token)
    {
        if (subjectId == Guid.Empty || context.IsTenantFilterBypassed)
            throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.NativeBoundaryRequired);
        try
        {
            RequireTransaction();
            if (await identity.IsSubjectFencedAsync(subjectId, token))
                throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.SubjectUnavailable);
        }
        catch (InvalidOperationException)
        {
            throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.NativeBoundaryRequired);
        }
        if (await Rows<User>().AnyAsync(row => row.Id == subjectId && row.IsDeleted, token))
            throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.SubjectUnavailable);
        // Qualification must observe committed native authority, not silently ignore staged
        // corrections/withdrawals in the same caller's tracker.
        if (HasUncommittedContinuityAuthority())
            throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.NativeBoundaryRequired);
    }

    private bool HasUncommittedContinuityAuthority() =>
        context.ChangeTracker.Entries().Any(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
            && entry.Entity is ModerationDecision or ModerationSanction or ModerationFinding
                or ModerationRecognitionPending or ModerationRecognitionCaptureItem
                or ModerationRecognitionPrivacySource or ModerationRecognitionPrivacyAdoption
                or ModerationQualificationMutationReceipt or ModerationPolicySelection
                or ModerationPolicyPublication or ModerationPolicyPublicationCandidate or ModerationQualificationEpoch);

    private async Task RequireSettledSubjectAsync(Guid subjectId, CancellationToken token)
    {
        var pending = await Rows<ModerationRecognitionPending>().Include(row => row.Material)
            .SingleOrDefaultAsync(row => row.SubjectId == subjectId, token);
        if (pending is null) return;
        pending.ValidatePersistedState();
        if (pending.AuthoritySequence is null || pending.Failure != ModerationRecognitionPendingFailure.None
            || pending.State == ModerationRecognitionPendingState.AuthorityBound && !pending.HasCompleteMaterial
            || pending.State is not (ModerationRecognitionPendingState.AuthorityBound or ModerationRecognitionPendingState.NoLongerRequired))
            throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.SettlementRequired);
    }

    private static void RequireReadableContinuityKey(ModerationRecognitionKeyRing ring, ModerationRecognitionCaptureItem item)
    {
        try
        {
            using var key = ring.BorrowReadableKey(item.TrustDomainId, item.KeyId, item.KeyGeneration);
            if (ring.RingGeneration < item.RingGeneration || ring.SerializationVersion != item.SerializationVersion
                || !SameKey(key.VerificationTag, item.KeyVerificationTag))
                throw new InvalidOperationException("moderation_recognition_original_key_unavailable");
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.KeyUnavailable);
        }
    }

    private static bool SameKey(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right));

    private static DateTimeOffset Earlier(DateTimeOffset? previous, DateTimeOffset next) =>
        previous is { } value && value < next ? value : next;

    private static ModerationRecognitionContinuityEvaluation PurposeFailure(
        IEnumerable<ModerationRecognitionCaptureItem> items, ModerationRecognitionContinuityFailure failure)
    {
        var result = new ModerationRecognitionContinuityEvaluation();
        foreach (var purpose in items.Select(row => row.Purpose).Distinct())
            result = WithPurposeFailure(result, purpose, failure);
        return result;
    }

    private static ModerationRecognitionContinuityEvaluation WithPurposeFailure(
        ModerationRecognitionContinuityEvaluation result, ModerationReferenceRecognition purpose,
        ModerationRecognitionContinuityFailure failure) =>
        purpose == ModerationReferenceRecognition.SeparatelyQualifiedFinalAction
            ? result with { ActionFailure = failure } : result with { AdmissionFailure = failure };

    private sealed class ContinuityUnavailable(ModerationRecognitionContinuityFailure failure) : Exception
    {
        public ModerationRecognitionContinuityFailure Failure { get; } = failure;
    }
}
