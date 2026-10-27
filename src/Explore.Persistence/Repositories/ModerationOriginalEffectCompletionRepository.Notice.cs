using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationOriginalEffectCompletionRepository
{
    private async Task<DateTimeOffset> NoticeAsync(ModerationSourceAuthority authority,
        ModerationEffectIntent effect, ModerationNoticeSource source, ModerationNoticeCaptureSnapshot capture,
        ModerationDecision decision, string selectionDigest, DateTimeOffset observedAt, DateTimeOffset until,
        List<string> facts, CancellationToken token)
    {
        var snapshot = capture.Notice!;
        if (clock.GetUtcNow() >= until)
            throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.OriginalCustody);
        var expected = new HashSet<(ModerationNoticeAudience Audience, int Kind, Guid Id)>
        {
            (ModerationNoticeAudience.Subject, (int)ModerationSubjectKind.Content, source.SubjectId)
        };
        foreach (var representative in capture.ContentRepresentatives)
            expected.Add((ModerationNoticeAudience.ContentRepresentative, 1, representative.HumanId));
        foreach (var reviewer in capture.Reviewers)
            expected.Add((ModerationNoticeAudience.Reviewer, 1, reviewer.ReviewerHumanId));
        var occurrences = await context.ModerationNotices.AsNoTracking().Where(row => row.EffectId == effect.Id)
            .OrderBy(row => row.Audience).ThenBy(row => row.RecipientSubjectId).ToListAsync(token);
        if (occurrences.Count != expected.Count
            || !expected.SetEquals(occurrences.Select(row => (row.Audience, row.RecipientSubjectKind, row.RecipientSubjectId)))
            || occurrences.Any(row => row.TenantId != authority.Tenant.Id || row.TrustDomainId != effect.TrustDomainId
                || row.CreatedAt != source.CapturedAt))
            throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.OriginalNotification);
        foreach (var occurrence in occurrences)
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationNotice>(context, occurrence.Id, token);
        var acknowledgement = await context.ModerationNoticeNotificationReceipts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.EffectId == effect.Id, token);
        if (acknowledgement is null
            || !acknowledgement.Matches(source, authority.Tenant.Id, snapshot.ContestationCalendarMonths, snapshot.RetainUntil)
            || acknowledgement.NotifiedAt > observedAt
            || !occurrences.Any(row => row.Id == acknowledgement.NoticeId
                && row.Audience == ModerationNoticeAudience.ContentRepresentative && row.RecipientSubjectKind == 1
                && row.RecipientSubjectId == acknowledgement.AcknowledgedByHumanId))
            throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.OriginalNotification);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationNoticeNotificationReceipt>(context, acknowledgement.Id, token);

        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationNoticeDisclosureApproval>(context, snapshot.ApprovalId, token);
        var privateReadAt = clock.GetUtcNow();
        if (privateReadAt >= until)
            throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.OriginalCustody);
        var approval = await context.ModerationNoticeDisclosureApprovals.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == snapshot.ApprovalId && row.RetainUntil > privateReadAt, token);
        if (approval is null || clock.GetUtcNow() >= until)
            throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.ProtectedContentRecourse);
        approval.ValidatePersistedState();
        if (approval.RevokedAt is not null || approval.Revision != snapshot.ApprovalRevision
            || approval.HostBindingId != authority.Binding.Id || approval.TrustDomainId != effect.TrustDomainId
            || approval.TenantId != authority.Tenant.Id || approval.EffectId != effect.Id
            || approval.DecisionId != effect.DecisionId || approval.DecisionRevision != effect.DecisionRevision
            || approval.CaseId != capture.Decision.CaseId || approval.PolicyId != snapshot.PolicyId
            || approval.PolicyRevision != snapshot.PolicyRevision || approval.PublicationId != snapshot.PublicationId
            || approval.ProfileId != snapshot.ProfileId || approval.ContributionId != snapshot.ContributionId
            || approval.SelectionDigest != selectionDigest
            || approval.ApprovedAt > source.CapturedAt || approval.RetainUntil != snapshot.RetainUntil
            || approval.ValidUntil <= observedAt || approval.RuleMeaning != snapshot.RuleMeaning.Value
            || approval.ReasonMeaning != snapshot.ReasonMeaning.Value || !approval.Intent.Facts.SequenceEqual(snapshot.Facts)
            || approval.RemainingActions != snapshot.RemainingActions.Value || approval.Recourse != snapshot.Recourse.Value
            || approval.Automation != snapshot.Automation.Value || approval.Retention != snapshot.Retention.Value
            || !approval.Intent.ContentRepresentatives.SequenceEqual(capture.ContentRepresentatives)
            || !authority.Administrators.Contains(approval.ApprovedByHumanId))
            throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.ProtectedContentRecourse);
        if (authority.NativeTargets?.RepresentativeAuthority is not { } representativePlan)
            throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.ProtectedContentRecourse);
        var custodyUntil = await ModerationNoticeContentCustody.AcquireAsync(context, privacy, decision.Subject,
            authority.Tenant, capture.ContentRepresentatives, authority.FencedHumans, observedAt,
            snapshot.RetainUntil, token, representativePlan);
        if (custodyUntil is null)
            throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.ProtectedContentRecourse);
        // The actual submission/read owners use this same native representative custody.
        // No response body or submission is read: choosing not to respond cannot defeat notice,
        // manufacture a finding or become a prerequisite for discharging this operational duty.
        facts.Add(Digest(occurrences.Select(row => new
        {
            row.Id,
            row.Audience,
            row.RecipientSubjectKind,
            row.RecipientSubjectId,
            row.CreatedAt
        }).ToArray()));
        facts.Add(Digest(acknowledgement));
        facts.Add(Digest(new { approval.Id, approval.Revision, approval.SelectionDigest, approval.RetainUntil }));
        return new[] { until, approval.ValidUntil, custodyUntil.Value }.Min();
    }
}
