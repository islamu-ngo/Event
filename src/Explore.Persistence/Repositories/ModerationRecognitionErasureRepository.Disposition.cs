using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationRecognitionErasureRepository : IModerationRecognitionRecoveryRepository
{
    public Task<ModerationRecognitionDisposition> AssessAsync(ModerationRecognitionRecoveryAssessment assessment,
        Guid authenticatedHuman, CancellationToken token) =>
        identity.ExecuteSerializedAsync(async ct =>
        {
            await using var transaction = await BeginAsync(ct);
            try
            {
                var pending = await RecoveryRequestAsync(assessment.IntentId, ct);
                var request = RecoveryRequest(pending);
                var authority = await AcquireSubjectAsync(request, ct);
                pending = (await ReadRequestAsync(request, ct))!;
                var coverage = await RequiredDispositionSourcesAsync(authority, pending, ct);
                if (!coverage.Contains(assessment.SourceId))
                    throw new InvalidOperationException("moderation_recognition_disposition_source_outside_request");
                var (source, adoption) = await QualifyDispositionSourceAsync(authority, pending, assessment.SourceId, ct);
                if (pending.Revision != assessment.ExpectedPendingRevision
                    || source.Revision != assessment.ExpectedSourceRevision
                    || source.WriterGeneration != assessment.ExpectedSourceGeneration
                    || adoption.Id != assessment.AdoptionId || adoption.Revision != assessment.ExpectedAdoptionRevision
                    || adoption.WriterGeneration != assessment.ExpectedAdoptionGeneration)
                    throw new InvalidOperationException("moderation_recognition_disposition_revision_conflict");
                var row = ModerationRecognitionDisposition.Assess(assessment.Id, pending, source, adoption,
                    authenticatedHuman, assessment.Grounds,
                    ModerationRecognitionDisposition.ComputeCoverageReceipt(coverage), coverage.Length,
                    clock.GetUtcNow(), assessment.ValidUntil);
                if (await Rows<ModerationRecognitionDisposition>().AnyAsync(item =>
                    item.IntentId == pending.IntentId && item.PendingRevision == pending.Revision
                    && item.SourceId == source.Id, ct))
                    throw new InvalidOperationException("moderation_recognition_disposition_already_assessed");
                context.Set<ModerationRecognitionDisposition>().Add(row);
                context.Set<ModerationRecognitionDispositionAcceptance>().Add(
                    ModerationRecognitionDispositionAcceptance.Record(row,
                        ModerationRecognitionDispositionAcceptanceRole.RecoveryOwner, authenticatedHuman));
                await context.SaveChangesAsync(ct);
                _ = await QualifyDispositionSourceAsync(authority, pending, source.Id, ct);
                if (clock.GetUtcNow() >= row.ValidUntil)
                    throw new InvalidOperationException("moderation_recognition_disposition_expired");
                await CommitAsync(transaction, ct);
                return row;
            }
            finally { context.ChangeTracker.Clear(); }
        }, token);

    public Task<ModerationRecognitionDisposition> AuthorizeAsync(Guid dispositionId, long expectedRevision,
        Guid authenticatedHuman, CancellationToken token) =>
        identity.ExecuteSerializedAsync(async ct =>
        {
            await using var transaction = await BeginAsync(ct);
            try
            {
                var discovered = await Rows<ModerationRecognitionDisposition>().SingleAsync(row => row.Id == dispositionId, ct);
                var pending = await RecoveryRequestAsync(discovered.IntentId, ct);
                var request = RecoveryRequest(pending);
                var authority = await AcquireSubjectAsync(request, ct);
                pending = (await ReadRequestAsync(request, ct))!;
                await RelationalEntityRowFence.AcquireGlobalAsync<ModerationRecognitionDisposition>(context, dispositionId, ct);
                var row = await Rows<ModerationRecognitionDisposition>().SingleAsync(item => item.Id == dispositionId, ct);
                var coverage = await RequiredDispositionSourcesAsync(authority, pending, ct);
                if (row.CoverageCount != coverage.Length
                    || row.CoverageReceipt != ModerationRecognitionDisposition.ComputeCoverageReceipt(coverage))
                    throw new InvalidOperationException("moderation_recognition_disposition_coverage_changed");
                var (source, adoption) = await QualifyDispositionSourceAsync(authority, pending, row.SourceId, ct);
                var now = clock.GetUtcNow();
                var acceptance = await Rows<ModerationRecognitionDispositionAcceptance>().SingleOrDefaultAsync(item =>
                    item.DispositionId == row.Id && item.Role == ModerationRecognitionDispositionAcceptanceRole.RecoveryOwner, ct);
                if (acceptance is null || !acceptance.Matches(row))
                    throw new InvalidOperationException("moderation_recognition_disposition_recovery_acceptance_missing");
                context.Attach(row);
                var authorityState = await erasure.GetStateAsync(ct);
                row.Authorize(expectedRevision, authenticatedHuman, pending, source, adoption, now,
                    authorityState.HighWaterSequence);
                context.Set<ModerationRecognitionDispositionAcceptance>().Add(
                    ModerationRecognitionDispositionAcceptance.Record(row,
                        ModerationRecognitionDispositionAcceptanceRole.PrivacyOwner, authenticatedHuman));
                context.Set<ModerationRecognitionDispositionRemedy>().Add(
                    ModerationRecognitionDispositionRemedy.Protect(row, pending, source, now));
                await context.SaveChangesAsync(ct);
                var terminal = await QualifyDispositionSourceAsync(authority, pending, source.Id, ct);
                if (!row.Authorizes(pending, terminal.Source, terminal.Adoption, clock.GetUtcNow()))
                    throw new InvalidOperationException("moderation_recognition_disposition_expired");
                await CommitAsync(transaction, ct);
                return row;
            }
            finally { context.ChangeTracker.Clear(); }
        }, token);

    public async Task<bool> HasLawfulDispositionAsync(PrivacyErasureRequest request, CancellationToken token)
    {
        RequireTransaction();
        if (request.IdentityFences.Count != 0 || request.IdentityKeyId is not null || request.IdentityKeyVerificationTag is not null)
            return false;
        var authority = await AcquireSubjectAsync(request, token);
        var pending = await ReadRequestAsync(request, token);
        if (pending is null || pending.AuthoritySequence is not null) return false;
        var rows = await ReadDispositionsAsync(pending, retainFences: true, token);
        if (rows.Length == 0) return false;
        var coverage = await RequiredDispositionSourcesAsync(authority, pending, token);
        if (!CompleteDispositionCoverage(rows, coverage)) return false;
        foreach (var row in rows)
        {
            if (row.Revision != 2 || !await HasAcceptanceReceiptsAsync(row, token)) return false;
            var qualified = await QualifyDispositionSourceAsync(authority, pending, row.SourceId, token);
            var proof = await Rows<ModerationRecognitionDispositionRemedy>().SingleOrDefaultAsync(item => item.Id == row.Id, token);
            if (proof is null || proof.IntentId != pending.IntentId || proof.PendingRevision != pending.Revision
                || proof.ProtectedAt != row.AuthorizedAt || proof.RetainUntil <= clock.GetUtcNow()
                || proof.CoverageReceipt != row.CoverageReceipt || proof.GroundsReceipt != row.GroundsReceipt
                || proof.LegalMandateReceipt != row.LegalMandateReceipt
                || !row.Authorizes(pending, qualified.Source, qualified.Adoption, clock.GetUtcNow())) return false;
        }
        return true;
    }

    public async Task BindLawfulDispositionAsync(PrivacyErasureIntent fact, CancellationToken token)
    {
        RequireTransaction();
        await RequireRetainedDispositionFactAsync(fact, token);
        var request = PrivacyErasureRequest.Create(fact.IntentId, fact.SubjectKind, fact.SubjectId, fact.ReasonCode, fact.PolicyVersion);
        _ = await identity.IsSubjectFencedAsync(request.SubjectId, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, request.SubjectId, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationRecognitionPending>(context, request.IntentId, token);
        var pending = await ReadRequestAsync(request, token)
            ?? throw new InvalidOperationException("moderation_recognition_disposition_request_missing");
        var rows = await ReadDispositionsAsync(pending, retainFences: true, token);
        if (CompleteDispositionCoverage(rows, rows.Select(row => row.SourceId).ToArray())
            && rows.All(row => row.MatchesFact(fact) && row.PreservesRequest(pending))
            && pending.Material.All(item => item.DroppedAt is not null)
            && await AllAcceptanceReceiptsAsync(rows, token)) return;
        // The native append held the original source fences through the retained commit.
        // Recovery verifies its frozen independent acceptances at that exact fact timestamp,
        // not today's policy lease. Later withdrawal cannot strand already committed erasure.
        if (!await AcceptedAtCommittedFactAsync(rows, pending, fact, retainFences: true, token))
            throw new InvalidOperationException("moderation_recognition_disposition_not_current");
        foreach (var row in rows)
        {
            context.Attach(row);
            row.Bind(fact, clock.GetUtcNow());
        }
        // Dropping eligibility is not deletion and never manufactures a computed receipt.
        foreach (var item in pending.Material.Where(item => item.DroppedAt is null))
        {
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationRecognitionCaptureItem>(context, item.Id, token);
            context.Attach(item);
            item.Drop(clock.GetUtcNow());
        }
        await context.SaveChangesAsync(token);
    }

    public async Task<bool> VerifyLawfulDispositionAsync(PrivacyErasureIntent fact, CancellationToken token)
    {
        RequireTransaction();
        await RequireRetainedDispositionFactAsync(fact, token);
        _ = await identity.IsSubjectFencedAsync(fact.SubjectId, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, fact.SubjectId, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationRecognitionPending>(context, fact.IntentId, token);
        var pending = await ReadRequestAsync(PrivacyErasureRequest.Create(
            fact.IntentId, fact.SubjectKind, fact.SubjectId, fact.ReasonCode, fact.PolicyVersion), token);
        if (pending is null) return false;
        var rows = await ReadDispositionsAsync(pending, retainFences: true, token);
        // Once the exact retained fact and consumption are durable, expiry/revocation cannot
        // resurrect erased inputs. Replay does not need a key or a new governance approval.
        return CompleteDispositionCoverage(rows, rows.Select(row => row.SourceId).ToArray())
            && rows.All(row => row.MatchesFact(fact) && row.PreservesRequest(pending))
            && pending.Material.All(item => item.DroppedAt is not null)
            && await AllAcceptanceReceiptsAsync(rows, token);
    }

    public async Task<bool> HasConsumedLawfulDispositionAsync(PrivacyErasureRequest request, CancellationToken token)
    {
        RequireTransaction();
        if (request.IdentityFences.Count != 0 || request.IdentityKeyId is not null || request.IdentityKeyVerificationTag is not null)
            return false;
        _ = await identity.IsSubjectFencedAsync(request.SubjectId, token);
        var pending = await ReadRequestAsync(request, token);
        if (pending is null) return false;
        var rows = await ReadDispositionsAsync(pending, retainFences: false, token);
        if (!CompleteDispositionCoverage(rows, rows.Select(row => row.SourceId).ToArray())
            || rows.Any(row => row.AuthoritySequence is null)
            || rows.Select(row => row.AuthoritySequence).Distinct().Count() != 1) return false;
        var fact = (await erasure.ReadAfterAsync(rows[0].AuthoritySequence!.Value - 1, 1, token)).SingleOrDefault();
        return fact is not null && rows.All(row => row.MatchesFact(fact) && row.PreservesRequest(pending))
            && pending.Material.All(item => item.DroppedAt is not null)
            && await AllAcceptanceReceiptsAsync(rows, token);
    }

    public async Task<bool> HasRecoverableLawfulDispositionAsync(PrivacyErasureRequest request, CancellationToken token)
    {
        RequireTransaction();
        if (request.IdentityFences.Count != 0 || request.IdentityKeyId is not null || request.IdentityKeyVerificationTag is not null)
            return false;
        _ = await identity.IsSubjectFencedAsync(request.SubjectId, token);
        var pending = await ReadRequestAsync(request, token);
        if (pending is null) return false;
        var rows = await ReadDispositionsAsync(pending, retainFences: false, token);
        if (!CompleteDispositionCoverage(rows, rows.Select(row => row.SourceId).ToArray())
            || rows.Any(row => row.Revision != 2 || row.AuthorityHighWaterBeforeAuthorization is null)
            || !await AllAcceptanceReceiptsAsync(rows, token)) return false;
        var state = await erasure.GetStateAsync(token);
        var after = rows.Max(row => row.AuthorityHighWaterBeforeAuthorization!.Value);
        if (after < state.RetainedFloorSequence)
            throw new InvalidOperationException("moderation_recognition_disposition_retained_proof_gap");
        // Retained identity serialization freezes this high-water. Reads are bounded pages,
        // not an unbounded caller cursor or a retry that manufactures a new authority fact.
        while (after < state.HighWaterSequence)
        {
            var facts = await erasure.ReadAfterAsync(after, 100, token);
            if (facts.Count == 0)
                throw new InvalidOperationException("moderation_recognition_disposition_retained_proof_gap");
            foreach (var fact in facts)
            {
                if (fact.AuthoritySequence <= after || fact.AuthoritySequence > state.HighWaterSequence)
                    throw new InvalidOperationException("moderation_recognition_disposition_retained_proof_gap");
                if (fact.IntentId == pending.IntentId)
                    return await AcceptedAtCommittedFactAsync(rows, pending, fact, retainFences: false, token);
                after = fact.AuthoritySequence;
            }
        }
        return false;
    }

    private async Task<bool> AcceptedAtCommittedFactAsync(ModerationRecognitionDisposition[] rows,
        ModerationRecognitionPending pending, PrivacyErasureIntent fact, bool retainFences, CancellationToken token)
    {
        if (!CompleteDispositionCoverage(rows, rows.Select(row => row.SourceId).ToArray())
            || rows.Any(row => !row.PreservesRequest(pending) || !row.AcceptsCommittedFact(fact))
            || !await AllAcceptanceReceiptsAsync(rows, token)) return false;
        foreach (var row in rows)
        {
            if (retainFences)
            {
                await RelationalEntityRowFence.AcquireGlobalAsync<ModerationRecognitionPrivacySource>(context, row.SourceId, token);
                await RelationalEntityRowFence.AcquireGlobalAsync<ModerationRecognitionPrivacyAdoption>(context, row.AdoptionId, token);
            }
            var source = await Rows<ModerationRecognitionPrivacySource>().Include(item => item.Retention)
                .SingleOrDefaultAsync(item => item.Id == row.SourceId, token);
            var adoption = await Rows<ModerationRecognitionPrivacyAdoption>().SingleOrDefaultAsync(item => item.Id == row.AdoptionId, token);
            if (source is null || adoption is null || !row.MatchesAcceptedSource(source, adoption, fact)) return false;
            var definition = await Rows<ModerationPolicyPublication>().SingleOrDefaultAsync(item =>
                item.Id == source.DefinitionId && item.HostBindingId == source.HostBindingId, token);
            if (definition is null
                || !await ReceiptAsync(source, ModerationRecognitionPrivacySourceOwner.RecordRequest(source, definition.Jurisdiction),
                    ModerationQualificationSourceKind.RecognitionPrivacySource, source.Id, source.RecordedByHumanId,
                    source.WriterGeneration, source.RecordedAt, token)
                || !await ReceiptAsync(source, ModerationRecognitionPrivacySourceOwner.AdoptionRequest(adoption, definition.Jurisdiction),
                    ModerationQualificationSourceKind.RecognitionPrivacyAdoption, adoption.Id, adoption.AdoptedByHumanId,
                    adoption.WriterGeneration, adoption.AdoptedAt, token)) return false;
            var remedy = await Rows<ModerationRecognitionDispositionRemedy>().SingleOrDefaultAsync(item => item.Id == row.Id, token);
            if (remedy is null || remedy.IntentId != pending.IntentId || remedy.PendingRevision != pending.Revision
                || remedy.GroundsReceipt != row.GroundsReceipt || remedy.LegalMandateReceipt != row.LegalMandateReceipt
                || remedy.CoverageReceipt != row.CoverageReceipt || remedy.ProtectedAt != row.AuthorizedAt) return false;
        }
        return true;
    }

    private async Task<bool> AllAcceptanceReceiptsAsync(ModerationRecognitionDisposition[] rows, CancellationToken token)
    {
        foreach (var row in rows)
            if (!await HasAcceptanceReceiptsAsync(row, token)) return false;
        return true;
    }

    private async Task<bool> HasAcceptanceReceiptsAsync(ModerationRecognitionDisposition row, CancellationToken token)
    {
        var receipts = await Rows<ModerationRecognitionDispositionAcceptance>()
            .Where(item => item.DispositionId == row.Id).ToArrayAsync(token);
        return receipts.Length == 2 && receipts.Select(item => item.Role).Distinct().Count() == 2
            && receipts.All(item => item.Matches(row));
    }

    public async Task<IReadOnlyList<ModerationRecognitionDispositionRemedy>> ReadRemediesAsync(byte[] originalReceiptHash,
        CancellationToken token)
    {
        if (originalReceiptHash is not { Length: 32 }) return [];
        var proofs = await Rows<ModerationRecognitionDispositionRemedy>()
            .Where(row => row.ReceiptHash == originalReceiptHash).ToListAsync(token);
        var now = clock.GetUtcNow();
        foreach (var proof in proofs) proof.ValidatePersistedState();
        return proofs.Where(row => row.Authenticates(originalReceiptHash, now))
            .OrderByDescending(row => row.ProtectedAt).ToArray();
    }

    private async Task<ModerationRecognitionDisposition[]> ReadDispositionsAsync(
        ModerationRecognitionPending pending, bool retainFences, CancellationToken token)
    {
        var rows = await Rows<ModerationRecognitionDisposition>().Where(item =>
            item.IntentId == pending.IntentId && item.PendingRevision == pending.Revision).OrderBy(item => item.Id).ToArrayAsync(token);
        foreach (var row in rows)
        {
            if (retainFences)
                await RelationalEntityRowFence.AcquireGlobalAsync<ModerationRecognitionDisposition>(context, row.Id, token);
            row.ValidatePersistedState();
        }
        return rows;
    }

    private async Task<Guid[]> RequiredDispositionSourcesAsync(CaptureAuthority authority,
        ModerationRecognitionPending pending, CancellationToken token)
    {
        // Every original captured source remains in the inventory even when its sanction
        // was reversed. A live contribution from another owner cannot disappear behind the
        // earliest request owner or the caller-selected source.
        var current = await ContributionsAsync(authority, pending.SubjectId, token);
        return pending.Material.Select(row => row.SourceId).Concat(current.Select(row => row.Source.Id))
            .Distinct().Order().ToArray();
    }

    private static bool CompleteDispositionCoverage(ModerationRecognitionDisposition[] rows, Guid[] sources) =>
        rows.Length > 0 && rows.Length == sources.Length && sources.Distinct().Count() == sources.Length
        && rows.Select(row => row.SourceId).Order().SequenceEqual(sources.Order())
        && rows.All(row => row.CoverageCount == sources.Length
            && row.CoverageReceipt == ModerationRecognitionDisposition.ComputeCoverageReceipt(sources));

    private async Task<(ModerationRecognitionPrivacySource Source, ModerationRecognitionPrivacyAdoption Adoption)>
        QualifyDispositionSourceAsync(CaptureAuthority authority, ModerationRecognitionPending pending,
            Guid sourceId, CancellationToken token)
    {
        var source = await Rows<ModerationRecognitionPrivacySource>().Include(row => row.Retention)
            .SingleOrDefaultAsync(row => row.Id == sourceId, token);
        if (source is null || authority.Host is null || source.HostBindingId != authority.Host.Id
            || source.RecordedAt > pending.ReceivedAt
            || new[] { source.RecordedByHumanId, source.PrivacyOwnerHumanId, source.RecoveryOwnerHumanId,
                source.PrivacyControllerHumanId, source.RecoveryControllerHumanId, source.GovernanceControllerHumanId }
                .Contains(pending.SubjectId)
            || await Rows<ModerationRecognitionPrivacySource>().AnyAsync(row =>
                row.HostBindingId == source.HostBindingId && row.TenantId == source.TenantId
                && row.PublicationId == source.PublicationId && row.ProfileId == source.ProfileId
                && row.ModeCode == source.ModeCode && row.WriterGeneration > source.WriterGeneration, token))
            throw new InvalidOperationException("moderation_recognition_disposition_owner_unavailable");
        foreach (var human in new[] { source.RecordedByHumanId, source.PrivacyOwnerHumanId, source.RecoveryOwnerHumanId,
            source.PrivacyControllerHumanId, source.RecoveryControllerHumanId, source.GovernanceControllerHumanId }.Distinct())
            if (await identity.IsSubjectFencedAsync(human, token)
                || await Rows<PrivacyErasureSaga>().AnyAsync(row => row.SubjectId == human, token))
                throw new InvalidOperationException("moderation_recognition_disposition_owner_unavailable");
        var definition = await ModerationPolicyPublicationGraph.Include(Rows<ModerationPolicyPublication>()
            .Where(row => row.Id == source.DefinitionId)).SingleOrDefaultAsync(token);
        var profile = definition?.Profiles.SingleOrDefault(row => row.ProfileId == source.ProfileId);
        var adoption = definition is null || profile is null ? null :
            await QualifySourceAsync(authority, source, definition, profile, clock.GetUtcNow(), token);
        if (adoption is null)
            throw new InvalidOperationException("moderation_recognition_disposition_qualification_unavailable");
        return (source, adoption);
    }

    private async Task RequireRetainedDispositionFactAsync(PrivacyErasureIntent fact, CancellationToken token)
    {
        var retained = (await erasure.ReadAfterAsync(fact.AuthoritySequence - 1, 1, token)).SingleOrDefault();
        if (retained is null || retained.AuthoritySequence != fact.AuthoritySequence || retained.IntentId != fact.IntentId
            || retained.SubjectId != fact.SubjectId || retained.SubjectKind != fact.SubjectKind
            || retained.PolicyVersion != fact.PolicyVersion || retained.ReasonCode != fact.ReasonCode
            || retained.RequestedAtUtc != fact.RequestedAtUtc || retained.RecordedAtUtc != fact.RecordedAtUtc
            || retained.RetentionExpiresAtUtc != fact.RetentionExpiresAtUtc
            || retained.IdentityFences.Count != 0 || fact.IdentityFences.Count != 0
            || retained.IsLegalHoldPseudonymized || fact.IsLegalHoldPseudonymized)
            throw new InvalidOperationException("moderation_recognition_disposition_authority_unavailable");
    }

    private async Task<ModerationRecognitionPending> RecoveryRequestAsync(Guid intentId, CancellationToken token) =>
        await Rows<ModerationRecognitionPending>().Include(row => row.Material)
            .SingleOrDefaultAsync(row => row.IntentId == intentId, token)
            ?? throw new InvalidOperationException("moderation_recognition_disposition_request_missing");

    private static PrivacyErasureRequest RecoveryRequest(ModerationRecognitionPending pending) =>
        PrivacyErasureRequest.Create(pending.IntentId, PrivacyErasureSubjectKind.User,
            pending.SubjectId, pending.ReasonCode, pending.PolicyVersion);
}
