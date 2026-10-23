using System.Data;
using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

/// <summary>
/// Stages the immutable source, portal occurrences and eligible native graph in the writer's
/// transaction. It never saves a decision alone, opens a second transaction or completes an effect.
/// </summary>
public sealed partial class ModerationNoticeCaptureRepository(
    ExploreDbContext context, IPrivacyIdentityFenceAuthority privacy, IModerationNoticeSourceOwner? sources,
    IModerationPolicyPublicationSelectionSource selections,
    ModerationNoticeRecipientRepository recipients, ModerationNoticeNativeMaterializer native,
    TimeProvider clock, IModerationAuthorityParticipant authority) : IModerationNoticeCapture
{
    public async Task<ModerationNoticeSource?> CaptureInCurrentTransactionAsync(
        IModerationSession session, Guid effectId, ModerationAuthorityGrant grant, CancellationToken cancellationToken)
    {
        var transaction = context.Database.CurrentTransaction;
        var tenantId = context.TenantFilterTenantId;
        if (transaction is null || !context.Database.IsRelational()
            || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable
            || context.IsTenantFilterBypassed || tenantId is null || tenantId == Guid.Empty
            || context.ModerationTrustDomainId != grant.Scope.TrustDomainId
            || !await ProviderTenantMatchesAsync(tenantId.Value, cancellationToken))
            return null;
        var effect = context.ModerationEffectIntents.Local.SingleOrDefault(row => row.Id == effectId);
        if (effect is null || effect.TrustDomainId != grant.Scope.TrustDomainId
            || effect.SubjectKind != (int)grant.Subject.Kind || effect.SubjectId != grant.Subject.Id
            || effect.PolicyRevision != grant.PolicyRevision || effect.QualificationRevision != grant.QualificationRevision
            || effect.OperationKind != (int)grant.Purpose)
            return null;
        var decision = context.ModerationDecisions.Local.SingleOrDefault(row => row.Id == effect.DecisionId);
        if (decision is null || !grant.Scope.Contains(decision.Scope) || decision.Subject != grant.Subject
            || decision.Revision != effect.DecisionRevision
            || (int)decision.Scope.Kind != effect.ScopeKind || decision.Scope.Id != effect.ScopeId)
            return null;

        var correction = effect.OperationKind == (int)ModerationOperationKind.Revoke
            ? await ModerationNoticeCorrectionAudience.ReadAsync(context, effect, decision, clock, cancellationToken)
            : null;
        if (effect.OperationKind == (int)ModerationOperationKind.Revoke && correction is null) return null;
        await using var fence = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.notice:{effectId:D}", cancellationToken);
        var existing = context.ModerationNoticeSources.Local.SingleOrDefault(row => row.EffectId == effectId)
            ?? await context.ModerationNoticeSources.AsNoTracking()
                .SingleOrDefaultAsync(row => row.EffectId == effectId, cancellationToken);
        if (existing is not null)
        {
            ModerationNoticeCaptureSnapshot? captured;
            try { captured = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(existing.SnapshotJson); }
            catch (JsonException) { return null; }
            if (!existing.Matches(effect) || captured is null || !captured.Matches(effect)
                || captured.WriterTenantId != tenantId)
                return null;
            if (existing.RetainUntil != captured.RetainUntil) return null;
            if (captured.PendingCorrection is not null)
                return await ReadPendingCorrectionAsync(existing, captured, correction, cancellationToken);
            if (captured.HasNoSubstantiveWork) return existing.TenantId == tenantId ? existing : null;
            var notices = await context.ModerationNotices.AsNoTracking()
                .Where(row => row.EffectId == effectId).ToListAsync(cancellationToken);
            notices.AddRange(context.ModerationNotices.Local.Where(row => row.EffectId == effectId));
            if (!notices.Any(row => row.Audience == ModerationNoticeAudience.Subject
                    && row.RecipientSubjectKind == effect.SubjectKind && row.RecipientSubjectId == effect.SubjectId)
                || !captured.Reviewers.All(proof => notices.Any(row => row.Audience == ModerationNoticeAudience.Reviewer
                    && row.RecipientSubjectKind == (int)ModerationSubjectKind.Human
                    && row.RecipientSubjectId == proof.ReviewerHumanId))
                || !captured.ContentRepresentatives.All(proof => notices.Any(row =>
                    row.Audience == ModerationNoticeAudience.ContentRepresentative
                    && row.RecipientSubjectKind == (int)ModerationSubjectKind.Human
                    && row.RecipientSubjectId == proof.HumanId))) return null;
            if (decision.Subject.Kind == ModerationSubjectKind.Content)
            {
                if (captured.ValidUntil is not { } deadline || captured.Notice is not { } notice
                    || decision.Subject.Origin.Kind != ModerationScopeKind.Tenant
                    || decision.Subject.Origin.Id != tenantId)
                    return null;
                var until = await AcquireContentCustodyAsync(session, effect, grant, decision.Subject,
                    decision.Subject.Origin, captured.ContentRepresentatives, clock.GetUtcNow(), deadline, cancellationToken);
                if (until is null || !await ProviderTenantMatchesAsync(tenantId.Value, cancellationToken)) return null;
                var now = clock.GetUtcNow();
                if (now < notice.CapturedAt || now >= deadline || now >= until.Value
                    || !ModerationNoticeRetention.CanNotifyAt(now, notice.ContestationCalendarMonths, notice.RetainUntil))
                    return null;
            }
            return existing;
        }

        // A missing historical source cannot be reconstructed by today's policy or mutable finding.
        // Only a newly staged committing effect is eligible for first capture.
        if (context.Entry(effect).State != EntityState.Added
            || !context.ModerationOperationReceipts.Local.Any(row => row.Id == effect.OperationId))
            return null;
        var sanction = effect.SanctionId is { } sanctionId
            ? context.ModerationSanctions.Local.SingleOrDefault(row => row.Id == sanctionId) : null;
        decision.ValidatePersistedState();
        sanction?.ValidatePersistedState();
        var hostBinding = await context.ModerationHostBindings.AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        if (hostBinding is null || hostBinding.TrustDomainId != effect.TrustDomainId) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationHostBinding>(context, hostBinding.Id, cancellationToken);
        hostBinding = await context.ModerationHostBindings.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == hostBinding.Id, cancellationToken);
        if (hostBinding is null || hostBinding.TrustDomainId != effect.TrustDomainId) return null;
        var epoch = await context.ModerationQualificationEpochs.AsNoTracking()
            .SingleOrDefaultAsync(row => row.HostBindingId == hostBinding.Id && row.TenantId == tenantId, cancellationToken);
        if (epoch is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEpoch>(context, epoch.Id, cancellationToken);
        epoch = await context.ModerationQualificationEpochs.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == epoch.Id, cancellationToken);
        if (epoch is null || epoch.Revision != grant.QualificationRevision) return null;

        var original = new ModerationNoticeOriginalDecision(decision.Id, decision.Revision, decision.CaseId,
            decision.State, ModerationNoticeScope.From(decision.Scope), decision.Subject.Kind, decision.Subject.Id,
            ModerationNoticeScope.From(decision.Subject.Origin),
            new(decision.Rule.OwnerId, decision.Rule.Code, decision.Rule.Revision), decision.CapabilityMask,
            decision.StartsAt, decision.EndsAt, decision.ReviewAt, decision.FindingId, decision.Approvals.Count,
            effect.SanctionId, effect.SupersededSanctionId);
        var capture = new ModerationNoticeCaptureSnapshot(2, "event.moderation.capture.v2",
            hostBinding.Id, hostBinding.SolutionId, hostBinding.InstanceId, tenantId.Value,
            epoch.Id, effect.QualificationRevision, original, null, null, [], null);
        if (correction is not null)
        {
            capture = capture with { PendingCorrection = correction, ValidUntil = correction.RetainUntil };
            if (!capture.Matches(effect) || clock.GetUtcNow() >= correction.RetainUntil) return null;
            var pending = ModerationNoticeSource.Capture(effect, tenantId, JsonSerializer.Serialize(capture), correction.RetainUntil);
            context.ModerationNoticeSources.Add(pending);
            foreach (var recipient in correction.Recipients)
                context.ModerationNotices.Add(ModerationNotice.CreatePendingCorrection(Guid.CreateVersion7(), pending,
                    recipient.Audience, recipient.RecipientSubjectKind, recipient.RecipientSubjectId));
            return pending;
        }
        if (capture.HasNoSubstantiveWork)
        {
            // A proposal may name future restrictions. Without a finding, approval or sanction it
            // selects no substantive plan. Retain that fact, not an invented disclosure approval.
            // This is writer ownership, not a tenant notice projection. Instance jurisdiction
            // still needs the real committing tenant so its empty effect can be leased safely.
            var inert = ModerationNoticeSource.Capture(effect, tenantId, JsonSerializer.Serialize(capture), null);
            context.ModerationNoticeSources.Add(inert);
            return inert;
        }
        if (sources is null || decision.FindingId is not { } findingId) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationFinding>(context, findingId, cancellationToken);
        var finding = await context.ModerationFindings.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == findingId, cancellationToken);
        if (finding is null || finding.CaseId != decision.CaseId || finding.Subject != decision.Subject
            || finding.Scope != decision.Scope || finding.Rule != decision.Rule) return null;
        finding.ValidatePersistedState();
        var observedAt = clock.GetUtcNow();
        var observed = await sources.AcquireAsync(transaction.TransactionId, effect, decision, sanction,
            grant, observedAt, cancellationToken);
        if (observed is null || !Current()) return null;

        var selected = await selections.AcquireAsync(transaction.TransactionId, hostBinding.Id,
            decision.Scope, decision.Rule, effect.PolicyRevision, false, cancellationToken)
            ?? await selections.AcquireAsync(transaction.TransactionId, hostBinding.Id,
                decision.Scope, decision.Rule, effect.PolicyRevision, true, cancellationToken);
        if (selected is null) return null;
        var snapshot = ModerationNoticeSnapshotFactory.Create(transaction.TransactionId, effect, decision,
            sanction, selected.Draft, observed, observedAt);
        if (snapshot is null || snapshot.Reason != new ModerationNoticeRule(
                finding.Reason.OwnerId, finding.Reason.Code, finding.Reason.Revision))
            return null;
        var tenant = AuthorityScope.Tenant(AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(hostBinding.TrustDomainId), hostBinding.SolutionId), hostBinding.InstanceId),
            tenantId.Value);
        var representatives = observed.Approval.ContentRepresentatives;
        var custodyUntil = await AcquireContentCustodyAsync(session, effect, grant, decision.Subject,
            tenant, representatives, observedAt, observed.ValidUntil, cancellationToken);
        if (custodyUntil is null || !Current() || !Fresh()) return null;
        var reviewers = await recipients.ReadReviewersAsync(decision, observed.ReviewerQualification,
            observedAt, cancellationToken);
        // The source unit does not invent an administrator fallback for an unowned queue.
        if (reviewers.Count == 0 || !Current() || !Fresh()) return null;

        var evidenceIds = reviewers.SelectMany(row => new[] { row.IdentityEvidenceId!.Value, row.ConflictEvidenceId!.Value })
            .Distinct().ToArray();
        var evidenceRevisions = await context.ModerationQualificationEvidence.AsNoTracking()
            .Where(row => evidenceIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id, row => row.Revision, cancellationToken);
        if (evidenceRevisions.Count != evidenceIds.Length) return null;
        capture = capture with
        {
            Finding = new(finding.Id, finding.Revision, finding.State, snapshot.Reason),
            Notice = snapshot,
            Reviewers = reviewers.Select(row => new ModerationNoticeReviewerProof(
                row.Id, row.Revision, row.ReviewerHumanId, row.ControllerHumanId!.Value,
                row.IdentityEvidenceId!.Value, evidenceRevisions[row.IdentityEvidenceId.Value], row.QualifiedByHumanId!.Value,
                row.ConflictEvidenceId!.Value, evidenceRevisions[row.ConflictEvidenceId.Value],
                row.AssessedByHumanId!.Value, row.QualifiedUntil!.Value))
                .ToImmutableArray(),
            ReviewerPolicySource = observed.ReviewerQualification.CurrentSource,
            ContentRepresentatives = representatives,
            ValidUntil = new[] { custodyUntil.Value, observed.ValidUntil, observed.Approval.ValidUntil,
                    observed.ReviewerQualification.ValidUntil }
                .Concat(observed.Targets.Select(row => row.ValidUntil))
                .Concat(reviewers.Select(row => row.QualifiedUntil!.Value)).Min()
        };
        var source = ModerationNoticeSource.Capture(
            effect, decision.ScopeTenantId, JsonSerializer.Serialize(capture), snapshot.RetainUntil);
        context.ModerationNoticeSources.Add(source);
        await AddNoticeAsync(ModerationNoticeAudience.Subject, (int)decision.Subject.Kind,
            decision.Subject.Id, cancellationToken);
        foreach (var representative in representatives.OrderBy(row => row.HumanId))
            await AddNoticeAsync(ModerationNoticeAudience.ContentRepresentative, (int)ModerationSubjectKind.Human,
                representative.HumanId, cancellationToken);
        foreach (var reviewer in reviewers.DistinctBy(row => row.ReviewerHumanId).OrderBy(row => row.ReviewerHumanId))
            await AddNoticeAsync(ModerationNoticeAudience.Reviewer, (int)ModerationSubjectKind.Human,
                reviewer.ReviewerHumanId, cancellationToken);
        // Native graph persistence may flush the shared context, but cannot commit this transaction.
        // The writer must roll back on null, including partial flushes and expiry during materialization.
        var currentCustody = await AcquireContentCustodyAsync(session, effect, grant, decision.Subject,
            tenant, representatives, clock.GetUtcNow(), custodyUntil.Value, cancellationToken);
        if (currentCustody is null || !await ProviderTenantMatchesAsync(tenantId.Value, cancellationToken)) return null;
        var completedAt = clock.GetUtcNow();
        return Current() && Fresh() && capture.ValidUntil is { } captureDeadline
            && completedAt < currentCustody.Value && completedAt < captureDeadline
            && ModerationNoticeRetention.CanNotifyAt(completedAt, snapshot.ContestationCalendarMonths, snapshot.RetainUntil)
            && reviewers.All(row => row.IsQualifiedAt(completedAt)) ? source : null;

        bool Current() => ReferenceEquals(context.Database.CurrentTransaction, transaction)
            && transaction.GetDbTransaction().Connection == context.Database.GetDbConnection()
            && !context.IsTenantFilterBypassed && context.TenantFilterTenantId == tenantId
            && context.ModerationTrustDomainId == grant.Scope.TrustDomainId;
        bool Fresh()
        {
            var now = clock.GetUtcNow();
            return now >= observedAt && now < observed.ValidUntil && now < observed.Approval.ValidUntil
                && now < observed.ReviewerQualification.ValidUntil
                && observed.Targets.All(target => now < target.ValidUntil);
        }

        async Task AddNoticeAsync(ModerationNoticeAudience audience, int kind, Guid recipientId, CancellationToken token)
        {
            var noticeId = Guid.CreateVersion7();
            Guid? intentId = null;
            var disposition = source.TenantId is null ? ModerationNoticeDeliveryDisposition.NoTenantProjection
                : kind != (int)ModerationSubjectKind.Human ? ModerationNoticeDeliveryDisposition.NoHumanProjection
                : ModerationNoticeDeliveryDisposition.RecipientUnavailable;
            if (source.TenantId is { } nativeTenant && kind == (int)ModerationSubjectKind.Human)
            {
                var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
                // Subject origin is authoritative. Matching local GUIDs never establish identity linkage.
                var localSubject = host is not null && host.TrustDomainId == source.TrustDomainId
                    && (audience is ModerationNoticeAudience.Reviewer or ModerationNoticeAudience.ContentRepresentative
                    || decision.SubjectOriginKind == ModerationScopeKind.Instance
                        && decision.SubjectOriginTrustDomainId == host.TrustDomainId
                        && decision.SubjectOriginSolutionId == host.SolutionId
                        && decision.SubjectOriginInstanceId == host.InstanceId);
                if (localSubject)
                {
                    var user = await recipients.ReadTenantRecipientAsync(source.TrustDomainId, nativeTenant, recipientId, token);
                    if (user is not null)
                        (intentId, disposition) = await native.MaterializeAsync(source, noticeId, audience, user, token);
                }
            }
            context.ModerationNotices.Add(ModerationNotice.Create(noticeId, source, audience,
                kind, recipientId, intentId, disposition));
        }
    }

    private async Task<DateTimeOffset?> AcquireContentCustodyAsync(
        IModerationSession session, ModerationEffectIntent effect, ModerationAuthorityGrant grant,
        SubjectReference subject, AuthorityScope tenant, ImmutableArray<ModerationNoticeContentRepresentative> representatives,
        DateTimeOffset observedAt, DateTimeOffset validUntil, CancellationToken token)
    {
        if (subject.Kind != ModerationSubjectKind.Content)
            return await ModerationNoticeContentCustody.AcquireAsync(context, privacy, subject, tenant,
                representatives, [], observedAt, validUntil, token);
        if (authority is not ModerationAuthorityParticipant participant
            || await participant.BorrowNoticeSourceAuthorityAsync(context, session, effect, grant, token) is not { } acquired
            || acquired.NativeTargets?.RepresentativeAuthority is not { } plan)
            return null;
        return await ModerationNoticeContentCustody.AcquireAsync(context, privacy, subject, tenant,
            representatives, acquired.FencedHumans, observedAt, validUntil, token, plan);
    }

    private async Task<bool> ProviderTenantMatchesAsync(Guid tenantId, CancellationToken token)
    {
        if (context.Database.ProviderName != RelationalNamedLock.PostgreSqlProvider) return true;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT current_setting('app.current_tenant_id', true)";
        var value = await command.ExecuteScalarAsync(token);
        return value is string setting && Guid.TryParse(setting, out var actual) && actual == tenantId;
    }
}
