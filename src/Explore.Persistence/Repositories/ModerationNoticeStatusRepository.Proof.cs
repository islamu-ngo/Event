using Explore.Application.Contracts.Notifications;
using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationNoticeStatusRepository
{
    private async Task<QualifiedStatus?> ReadOwnedAsync(
        ModerationNoticeStatusRequest request, ModerationHostBinding host,
        EventAuthorityProviderOperations.AuthorityPlan? representativePlan, CancellationToken token)
    {
        var effect = await context.ModerationEffectIntents.AsNoTracking().SingleOrDefaultAsync(row =>
            row.Id == request.EffectId && row.DecisionId == request.DecisionId
            && row.TrustDomainId == host.TrustDomainId, token);
        var decision = await context.ModerationDecisions.AsNoTracking().SingleOrDefaultAsync(row =>
            row.Id == request.DecisionId, token);
        if (effect is null || decision is null) return null;
        try
        {
            effect.ValidatePersistedState();
            decision.ValidatePersistedState();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return null;
        }
        var instance = AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(host.TrustDomainId), host.SolutionId), host.InstanceId);
        var tenant = AuthorityScope.Tenant(instance, request.TenantId);
        if (!(decision.Subject.Kind == ModerationSubjectKind.Human && decision.SubjectId == request.SubjectId
                && decision.Subject.Origin == instance
            || decision.Subject.Kind == ModerationSubjectKind.Content && decision.Subject.Origin == tenant
                && decision.Scope.Kind == ModerationScopeKind.Resource && decision.Scope.Parent == tenant)
            || decision.Scope != instance && !tenant.Contains(decision.Scope)
            || effect.SubjectKind != (int)decision.Subject.Kind || effect.SubjectId != decision.SubjectId
            || effect.ScopeKind != (int)decision.Scope.Kind || effect.ScopeId != decision.Scope.Id
            || effect.DecisionRevision > decision.Revision || effect.CreatedAt < decision.CreatedAt
            || clock.GetUtcNow() < effect.CreatedAt)
            return null;
        // Read only an existence predicate over ownership coordinates, never case evidence.
        if (!await context.ModerationCases.AsNoTracking().AnyAsync(row => row.Id == decision.CaseId
            && row.SubjectKind == decision.SubjectKind && row.SubjectId == decision.SubjectId
            && row.SubjectOriginKind == decision.SubjectOriginKind
            && row.SubjectOriginTrustDomainId == decision.SubjectOriginTrustDomainId
            && row.SubjectOriginSolutionId == decision.SubjectOriginSolutionId
            && row.SubjectOriginInstanceId == decision.SubjectOriginInstanceId
            && row.SubjectOriginTenantId == decision.SubjectOriginTenantId
            && row.SubjectOriginCommunityId == decision.SubjectOriginCommunityId
            && row.SubjectOriginResourceId == decision.SubjectOriginResourceId
            && row.SubjectOriginEventId == decision.SubjectOriginEventId
            && row.ScopeKind == decision.ScopeKind && row.ScopeTrustDomainId == decision.ScopeTrustDomainId
            && row.ScopeSolutionId == decision.ScopeSolutionId && row.ScopeInstanceId == decision.ScopeInstanceId
            && row.ScopeTenantId == decision.ScopeTenantId && row.ScopeCommunityId == decision.ScopeCommunityId
            && row.ScopeResourceId == decision.ScopeResourceId
            && row.ScopeEventId == decision.ScopeEventId, token))
            return null;

        var source = await context.ModerationNoticeSources.AsNoTracking()
            .Where(row => row.EffectId == effect.Id && row.TrustDomainId == host.TrustDomainId
                && row.SubjectKind == effect.SubjectKind && row.SubjectId == effect.SubjectId)
            .Select(row => new
            {
                row.TenantId,
                row.RetainUntil,
                row.StatusMetadataJson,
                row.StatusMetadataHash,
                row.SnapshotHash,
                row.EffectBindingHash,
                row.OperationId,
                row.DecisionId,
                row.DecisionRevision,
                row.ScopeKind,
                row.ScopeId,
                row.CapturedAt
            }).SingleOrDefaultAsync(token);
        if (effect.OriginalNoticeCleanupId is not null)
        {
            // Original representative attribution is purged with its source. Current Event
            // management alone cannot reconstruct permission to historical subject status.
            if (decision.Subject.Kind == ModerationSubjectKind.Content) return null;
            if (!effect.HasOriginalNoticeCleanup(host.Id, request.TenantId, clock.GetUtcNow())
                || source is not null
                || await context.ModerationNotices.AsNoTracking().AnyAsync(row => row.EffectId == effect.Id, token)
                || await context.Set<ModerationNoticeNotificationReceipt>().AsNoTracking()
                    .AnyAsync(row => row.EffectId == effect.Id, token))
                return null;
            return new(new(effect, ModerationNoticeNotificationProof.PurposePurged, null, null,
                effect.OriginalNoticeRetainUntil), null);
        }

        if (source is null || source.RetainUntil is not { } ceiling)
        {
            // With no original writer binding, an instance-level ledger cannot borrow the
            // current tenant. Tenant-scoped ownership can still report unavailable proof.
            return decision.Subject.Kind == ModerationSubjectKind.Human && decision.ScopeTenantId == request.TenantId
                ? new(new(effect, ModerationNoticeNotificationProof.Unavailable, null, null, null), null)
                : null;
        }
        var metadata = ModerationNoticeSource.StatusMetadata.Read(source.StatusMetadataJson, source.StatusMetadataHash);
        DateTimeOffset? representativeUntil = null;
        if (decision.Subject.Kind == ModerationSubjectKind.Content)
        {
            var representative = metadata?.ContentRepresentatives.SingleOrDefault(row => row.HumanId == request.SubjectId);
            if (representative is null || representativePlan is null)
                return null;
            try
            {
                representativeUntil = await ModerationNoticeContentCustody.AcquireAsync(context, privacy, decision.Subject, tenant,
                    [representative], [request.SubjectId], clock.GetUtcNow(), ceiling, token, representativePlan);
            }
            catch (DbUpdateConcurrencyException) { return null; }
            if (representativeUntil is null) return null;
        }
        if (metadata is not null && (metadata.WriterTenantId != request.TenantId || metadata.HostBindingId != host.Id))
            return null;
        if (metadata is null || metadata.SnapshotHash != source.SnapshotHash
            || metadata.EffectBindingHash != source.EffectBindingHash
            || !metadata.Matches(effect, decision, host, request.TenantId, ceiling)
            || source.OperationId != effect.OperationId || source.DecisionId != effect.DecisionId
            || source.DecisionRevision != effect.DecisionRevision || source.ScopeKind != effect.ScopeKind
            || source.ScopeId != effect.ScopeId || source.CapturedAt != effect.CreatedAt
            || source.TenantId != decision.ScopeTenantId)
            return decision.ScopeTenantId == request.TenantId
                ? new(new(effect, ModerationNoticeNotificationProof.Unavailable, null, null, null), representativeUntil) : null;
        if (clock.GetUtcNow() >= ceiling)
            return new(new(effect, ModerationNoticeNotificationProof.Unavailable, null, null, ceiling), representativeUntil);

        var notification = await context.Set<ModerationNoticeNotificationReceipt>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.EffectId == effect.Id, token);
        if (notification is null)
            return new(new(effect, ModerationNoticeNotificationProof.Unproven, null,
                metadata.ContestationCalendarMonths, ceiling), representativeUntil);
        try { notification.ValidatePersistedState(); }
        catch (ArgumentException)
        {
            return new(new(effect, ModerationNoticeNotificationProof.Unavailable, null, null, ceiling), representativeUntil);
        }
        if (notification.TrustDomainId != host.TrustDomainId || notification.TenantId != request.TenantId
            || notification.SubjectId != decision.SubjectId || notification.SubjectKind != (int)decision.Subject.Kind
            || notification.SnapshotHash != metadata.SnapshotHash
            || (decision.Subject.Kind == ModerationSubjectKind.Human
                ? notification.AcknowledgedByHumanId != request.SubjectId
                : !metadata.ContentRepresentatives.Any(row => row.HumanId == notification.AcknowledgedByHumanId))
            || notification.ContestationCalendarMonths != metadata.ContestationCalendarMonths
            || notification.RetainUntil != ceiling || notification.NotifiedAt < effect.CreatedAt
            || notification.NotifiedAt > clock.GetUtcNow()
            || !await context.ModerationNotices.AsNoTracking().AnyAsync(row => row.Id == notification.NoticeId
                && row.EffectId == effect.Id && row.TrustDomainId == host.TrustDomainId
                && row.Audience == (decision.Subject.Kind == ModerationSubjectKind.Human
                    ? ModerationNoticeAudience.Subject : ModerationNoticeAudience.ContentRepresentative)
                && row.RecipientSubjectKind == (int)ModerationSubjectKind.Human
                && row.RecipientSubjectId == notification.AcknowledgedByHumanId
                && row.TenantId == source.TenantId && row.CreatedAt == effect.CreatedAt, token))
            return new(new(effect, ModerationNoticeNotificationProof.Unavailable, null, null, ceiling), representativeUntil);
        return new(new(effect, ModerationNoticeNotificationProof.Proven, notification,
            metadata.ContestationCalendarMonths, ceiling), representativeUntil);
    }
}
