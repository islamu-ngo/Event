using System.Data;
using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationQualificationRepository
{
    private async Task<ContentResponseProof?> ReadContentResponseProofAsync(
        Guid tenantId, Guid noticeId, ContentResponseProof? acquired, CancellationToken token)
    {
        if (machine.IsMachineCaller || await admin.ResolveUserIdAsync(token) is not { } user)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unauthenticated);
        if (context.Database.CurrentTransaction is not { } transaction
            || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable
            || context.IsTenantFilterBypassed || context.TenantFilterTenantId != tenantId)
            return null;
        // Retained erasure, not just a not-yet-created primary saga, closes private capture.
        if (await privacy.IsSubjectFencedAsync(user, token)) return null;
        var host = await EventAuthorityProviderOperations.ReadWithoutRetainingLocksAsync(context,
            native => native.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token), token);
        if (host is null) return null;
        var instance = AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(host.TrustDomainId), host.SolutionId), host.InstanceId);
        var tenant = AuthorityScope.Tenant(instance, tenantId);
        var authorityOwner = new ModerationQualificationAuthority(context, admin, machine);
        var authority = await authorityOwner.AcquireContentResponseAsync(tenant, noticeId, acquired?.Authority, token);
        if (authority is null || authority.UserId != user || authority.Binding.Id != host.Id
            || await privacy.IsSubjectFencedAsync(user, token)
            || await context.PrivacyErasureSagas.AsNoTracking().AnyAsync(row => row.SubjectId == user, token))
            return null;
        var notice = await context.ModerationNotices.AsNoTracking().SingleOrDefaultAsync(row =>
            row.Id == noticeId && row.TenantId == tenantId && row.TrustDomainId == host.TrustDomainId
            && row.Audience == ModerationNoticeAudience.ContentRepresentative
            && row.RecipientSubjectKind == 1 && row.RecipientSubjectId == user, token);
        if (notice is null) return null;
        var effect = await context.ModerationEffectIntents.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == notice.EffectId, token);
        if (effect is null || effect.SubjectKind != (int)ModerationSubjectKind.Content
            || effect.OriginalNoticeCleanupId is not null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationDecision>(context, effect.DecisionId, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationEffectIntent>(context, effect.Id, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationNotice>(context, notice.Id, token);
        await using var sourceFence = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.notice:{effect.Id:D}", token);
        // Under the original source fence, qualify only ownership/retention metadata first.
        // Neither an initial request nor final commit revalidation may select expired
        // SnapshotJson merely to discover that its private purpose has ended.
        var envelope = await context.ModerationNoticeSources.AsNoTracking()
            .Where(row => row.EffectId == effect.Id)
            .Select(row => new
            {
                row.TenantId,
                row.TrustDomainId,
                row.SubjectKind,
                row.SubjectId,
                row.CapturedAt,
                row.RetainUntil,
                row.StatusMetadataJson,
                row.StatusMetadataHash,
                row.SnapshotHash,
                row.EffectBindingHash
            }).SingleOrDefaultAsync(token);
        var now = clock.GetUtcNow();
        if (envelope is null || envelope.TenantId != tenantId || envelope.TrustDomainId != host.TrustDomainId
            || envelope.SubjectKind != (int)ModerationSubjectKind.Content || envelope.SubjectId != effect.SubjectId
            || envelope.CapturedAt != notice.CreatedAt || envelope.CapturedAt != effect.CreatedAt
            || envelope.RetainUntil is not { } ceiling || now < envelope.CapturedAt || now >= ceiling)
            return null;
        var metadata = ModerationNoticeSource.StatusMetadata.Read(envelope.StatusMetadataJson, envelope.StatusMetadataHash);
        if (metadata is null || metadata.HostBindingId != host.Id || metadata.WriterTenantId != tenantId
            || metadata.SnapshotHash != envelope.SnapshotHash || metadata.EffectBindingHash != envelope.EffectBindingHash
            || !metadata.ContentRepresentatives.Any(row => row.HumanId == user)
            || await privacy.IsSubjectFencedAsync(user, token) || clock.GetUtcNow() >= ceiling)
            return null;
        var privateReadAt = clock.GetUtcNow();
        var source = await context.ModerationNoticeSources.AsNoTracking()
            .SingleOrDefaultAsync(row => row.EffectId == effect.Id && row.TenantId == tenantId
                && row.TrustDomainId == host.TrustDomainId && row.RetainUntil == ceiling
                && row.RetainUntil > privateReadAt && row.SnapshotHash == envelope.SnapshotHash, token);
        if (source is null || !source.Matches(effect) || source.CapturedAt != envelope.CapturedAt
            || clock.GetUtcNow() >= ceiling)
            return null;
        ModerationNoticeCaptureSnapshot? capture;
        try { capture = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(source.SnapshotJson); }
        catch (JsonException) { return null; }
        if (capture is null || !capture.Matches(effect) || capture.HasNoSubstantiveWork
            || capture.HostBindingId != host.Id || capture.HostSolutionId != host.SolutionId
            || capture.HostInstanceId != host.InstanceId || capture.WriterTenantId != tenantId
            || capture.Notice is not { } snapshot || snapshot.RetainUntil != ceiling
            || capture.Decision.SubjectKind != ModerationSubjectKind.Content
            || capture.Decision.SubjectId != source.SubjectId
            || capture.Decision.SubjectOrigin != ModerationNoticeScope.From(tenant)
            || capture.Decision.Scope.Kind != ModerationScopeKind.Resource
            || capture.Decision.Scope.Parent != ModerationNoticeScope.From(tenant))
            return null;
        var representative = capture.ContentRepresentatives.SingleOrDefault(row => row.HumanId == user);
        if (representative is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationNoticeDisclosureApproval>(
            context, snapshot.ApprovalId, token);
        var approval = await context.ModerationNoticeDisclosureApprovals.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == snapshot.ApprovalId, token);
        if (approval is null) return null;
        approval.ValidatePersistedState();
        if (approval.RevokedAt is not null || approval.Revision != snapshot.ApprovalRevision
            || approval.HostBindingId != host.Id || approval.TrustDomainId != host.TrustDomainId
            || approval.TenantId != tenantId || approval.ApprovedAt > source.CapturedAt
            || approval.EffectId != effect.Id || approval.DecisionId != source.DecisionId
            || approval.DecisionRevision != source.DecisionRevision || approval.CaseId != capture.Decision.CaseId
            || approval.PolicyId != snapshot.PolicyId || approval.PolicyRevision != snapshot.PolicyRevision
            || approval.PublicationId != snapshot.PublicationId || approval.ProfileId != snapshot.ProfileId
            || approval.ContributionId != snapshot.ContributionId || approval.RetainUntil != ceiling
            || approval.RuleMeaning != snapshot.RuleMeaning.Value || approval.ReasonMeaning != snapshot.ReasonMeaning.Value
            || !approval.Intent.Facts.SequenceEqual(snapshot.Facts)
            || approval.RemainingActions != snapshot.RemainingActions.Value || approval.Recourse != snapshot.Recourse.Value
            || approval.Automation != snapshot.Automation.Value || approval.Retention != snapshot.Retention.Value
            || !approval.Intent.ContentRepresentatives.SequenceEqual(capture.ContentRepresentatives))
            return null;
        // Neither a reversed finding nor an expired sanction removes the protected case operation.
        // Current case ownership must still be the original Content subject and resource scope.
        var decision = await context.ModerationDecisions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == source.DecisionId, token);
        var allegation = await context.ModerationCases.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == approval.CaseId, token);
        if (decision is null || allegation is null || decision.CaseId != allegation.Id
            || decision.Subject.Kind != ModerationSubjectKind.Content || decision.Subject.Id != source.SubjectId
            || decision.Subject.Origin != tenant || decision.Subject != allegation.Subject
            || decision.Scope != allegation.Scope || decision.Revision < source.DecisionRevision
            || ModerationNoticeScope.From(decision.Scope) != capture.Decision.Scope)
            return null;
        if (authority.DisclosureAuthority is null) return null;
        var qualifiedUntil = await ModerationNoticeContentCustody.AcquireAsync(context, privacy,
            decision.Subject, tenant, [representative], authority.FencedHumans, now, ceiling, token,
            authority.DisclosureAuthority);
        if (qualifiedUntil is null) return null;
        var acknowledgement = await context.Set<ModerationNoticeNotificationReceipt>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.EffectId == effect.Id, token);
        if (acknowledgement is not null
            && (!acknowledgement.Matches(source, tenantId, snapshot.ContestationCalendarMonths, ceiling)
                || acknowledgement.NotifiedAt > now
                || !await context.ModerationNotices.AsNoTracking().AnyAsync(row =>
                    row.Id == acknowledgement.NoticeId && row.EffectId == effect.Id
                    && row.TrustDomainId == host.TrustDomainId && row.TenantId == tenantId
                    && row.Audience == ModerationNoticeAudience.ContentRepresentative
                    && row.RecipientSubjectKind == 1 && row.RecipientSubjectId == acknowledgement.AcknowledgedByHumanId
                    && row.CreatedAt == source.CapturedAt, token)))
            return null;
        // A response is permitted before acknowledgement, including when a first notification
        // can no longer fit its calendar floor. It does not start or shorten that floor.
        var availability = new ModerationContentResponseAvailability(notice.Id, effect.Id, allegation.Id,
            source.SubjectId, representative.AssignmentId, representative.AssignmentRevision, source.SnapshotHash,
            ceiling, snapshot.ContestationCalendarMonths, acknowledgement?.NotifiedAt,
            acknowledgement?.RemedyUntil, qualifiedUntil.Value);
        var qualificationRevision = await context.ModerationQualificationEpochs.AsNoTracking()
            .Where(row => row.Id == authority.Epoch.Id && row.HostBindingId == host.Id && row.TenantId == tenantId)
            .Select(row => (long?)row.Revision).SingleOrDefaultAsync(token);
        if (!await authorityOwner.ProviderTenantMatchesAsync(tenantId, token)
            || await admin.ResolveUserIdAsync(token) != user || machine.IsMachineCaller
            || context.ModerationTrustDomainId != host.TrustDomainId || await privacy.IsSubjectFencedAsync(user, token)
            || qualificationRevision != authority.Epoch.Revision)
            return null;
        var currentTime = clock.GetUtcNow();
        if (currentTime < now || currentTime >= qualifiedUntil.Value) return null;
        return new(authority, qualificationRevision.Value, notice, source, approval, representative, availability, now);
    }
}
