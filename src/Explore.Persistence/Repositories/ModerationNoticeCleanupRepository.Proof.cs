using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationNoticeCleanupRepository
{
    private async Task<bool> ProvesOriginalWriteAsync(
        ModerationNoticeDisclosureApproval approval, ModerationNoticeSource source,
        ModerationEffectIntent effect, IReadOnlyList<ModerationNotice> occurrences,
        ModerationHostBinding host, CancellationToken token)
    {
        if (!source.Matches(effect) || source.RetainUntil != approval.RetainUntil
            || effect.DecisionId != approval.DecisionId || effect.DecisionRevision != approval.DecisionRevision)
            return false;
        ModerationNoticeCaptureSnapshot? capture;
        try { capture = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(source.SnapshotJson); }
        catch (JsonException) { return false; }
        if (capture is null || !capture.Matches(effect) || capture.HasNoSubstantiveWork
            || capture.HostBindingId != host.Id || capture.HostSolutionId != host.SolutionId
            || capture.HostInstanceId != host.InstanceId || capture.WriterTenantId != approval.TenantId
            || capture.Notice is not { } snapshot || snapshot.RetainUntil != source.RetainUntil
            || snapshot.RuleMeaning is null || snapshot.ReasonMeaning is null || snapshot.Facts.IsDefault
            || snapshot.RemainingActions is null || snapshot.Recourse is null
            || snapshot.Automation is null || snapshot.Retention is null
            || snapshot.ApprovalId != approval.Id || snapshot.ApprovalRevision != 1
            || approval.ApprovedAt > source.CapturedAt || approval.CaseId != capture.Decision.CaseId
            || snapshot.PolicyId != approval.PolicyId || snapshot.PolicyRevision != approval.PolicyRevision
            || snapshot.PublicationId != approval.PublicationId || snapshot.ProfileId != approval.ProfileId
            || snapshot.ContributionId != approval.ContributionId
            || snapshot.RuleMeaning.Value != approval.RuleMeaning || snapshot.ReasonMeaning.Value != approval.ReasonMeaning
            || !snapshot.Facts.SequenceEqual(approval.Intent.Facts)
            || snapshot.RemainingActions.Value != approval.RemainingActions || snapshot.Recourse.Value != approval.Recourse
            || snapshot.Automation.Value != approval.Automation || snapshot.Retention.Value != approval.Retention)
            return false;
        if (!capture.ContentRepresentatives.SequenceEqual(approval.Intent.ContentRepresentatives))
            return false;
        var decision = await context.ModerationDecisions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == effect.DecisionId, token);
        var receipt = await context.ModerationOperationReceipts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == effect.OperationId, token);
        var instance = AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(host.TrustDomainId), host.SolutionId), host.InstanceId);
        var tenant = AuthorityScope.Tenant(instance, approval.TenantId);
        if (decision is null || receipt is null || decision.Scope != instance && !tenant.Contains(decision.Scope)
            || decision.CaseId != capture.Decision.CaseId || source.TenantId != decision.ScopeTenantId
            || ModerationNoticeScope.From(decision.Scope) != capture.Decision.Scope
            || decision.SubjectKind != capture.Decision.SubjectKind || decision.SubjectId != capture.Decision.SubjectId
            || ModerationNoticeScope.From(decision.Subject.Origin) != capture.Decision.SubjectOrigin
            || receipt.DecisionId != effect.DecisionId || receipt.SanctionId != effect.SanctionId
            || receipt.TrustDomainId != effect.TrustDomainId || receipt.ScopeKind != effect.ScopeKind
            || receipt.ScopeId != effect.ScopeId || receipt.SubjectKind != effect.SubjectKind
            || receipt.SubjectId != effect.SubjectId || receipt.PolicyRevision != effect.PolicyRevision
            || receipt.QualificationRevision != effect.QualificationRevision
            || receipt.ResultRevision != (effect.OperationKind is (int)ModerationOperationKind.Revoke
                    or (int)ModerationOperationKind.Reconcile ? effect.SanctionRevision : effect.DecisionRevision))
            return false;
        var reviewers = capture.Reviewers.Select(row => row.ReviewerHumanId).Distinct().ToArray();
        return occurrences.Count == reviewers.Length + capture.ContentRepresentatives.Length + 1
            && occurrences.All(row => row.EffectId == source.EffectId && row.TrustDomainId == source.TrustDomainId
                && row.TenantId == source.TenantId && row.CreatedAt == source.CapturedAt)
            && occurrences.Count(row => row.Audience == ModerationNoticeAudience.Subject
                && row.RecipientSubjectKind == effect.SubjectKind && row.RecipientSubjectId == effect.SubjectId) == 1
            && reviewers.All(id => occurrences.Count(row => row.Audience == ModerationNoticeAudience.Reviewer
                && row.RecipientSubjectKind == (int)ModerationSubjectKind.Human && row.RecipientSubjectId == id) == 1)
            && capture.ContentRepresentatives.All(proof => occurrences.Count(row =>
                row.Audience == ModerationNoticeAudience.ContentRepresentative
                && row.RecipientSubjectKind == (int)ModerationSubjectKind.Human
                && row.RecipientSubjectId == proof.HumanId) == 1);
    }
}
