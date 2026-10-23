using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed record ModerationNoticeNativeQualification(
    ModerationNoticeContentApproval Content, ModerationNoticeReviewerQualification Reviewers,
    DateTimeOffset ValidUntil);

public sealed partial class ModerationAuthorityParticipant
{
    /// <summary>
    /// Reads separately approved text and current procedural policy on the already acquired
    /// lifecycle transaction. This does not qualify a contribution or its native targets.
    /// </summary>
    public async Task<ModerationNoticeNativeQualification?> AcquireNoticeQualificationAsync(
        Guid transactionId, ModerationPolicyPublication publication, ModerationEffectIntent effect,
        ModerationDecision decision, ModerationAuthorityGrant grant, DateTimeOffset observedAt,
        CancellationToken token)
    {
        var acquired = _acquisition;
        // Revoke/Reconcile change the contribution only; their expected revision can
        // name the sanction, so retain the actual decision anchor from acquisition.
        var expectedDecisionRevision = acquired?.Operation.Kind switch
        {
            ModerationOperationKind.Propose => 1L,
            ModerationOperationKind.Revoke or ModerationOperationKind.Reconcile => acquired?.DecisionRevision,
            _ => acquired?.DecisionRevision is { } revision && revision < long.MaxValue ? revision + 1 : (long?)null
        };
        if (acquired is null || !ReferenceEquals(acquired.Grant, grant)
            || acquired.Transaction.TransactionId != transactionId
            || context.Database.CurrentTransaction?.TransactionId != transactionId
            || acquired.Case is not { } moderationCase || acquired.Rule is not { } rule
            || publication.Id != acquired.Policy.Publication.PublicationId
            || publication.PolicyId != acquired.Policy.Publication.PolicyId
            || publication.Revision != grant.PolicyRevision
            || publication.HostBindingId != acquired.Source.Binding.Id
            || publication.Jurisdiction != acquired.Policy.Publication.Jurisdiction
            || publication.WriterGeneration != acquired.Policy.Publication.WriterGeneration
            || publication.CatalogueRevision != acquired.Policy.Publication.CatalogueRevision
            || effect.Id != acquired.Operation.EffectId || effect.OperationId != acquired.Operation.OperationId
            || effect.DecisionId != acquired.Operation.DecisionId || effect.DecisionId != decision.Id
            || effect.DecisionRevision != decision.Revision || decision.Revision != expectedDecisionRevision
            || effect.PolicyRevision != grant.PolicyRevision || effect.QualificationRevision != grant.QualificationRevision
            || effect.OperationKind != (int)grant.Purpose || effect.CompletedAt is not null
            || effect.CreatedAt > observedAt || effect.TrustDomainId != acquired.Source.Binding.TrustDomainId
            || effect.ScopeKind != (int)decision.Scope.Kind || effect.ScopeId != decision.Scope.Id
            || effect.SubjectKind != (int)decision.Subject.Kind || effect.SubjectId != decision.Subject.Id
            || decision.CaseId != moderationCase.Id || decision.Subject != grant.Subject
            || decision.Scope != acquired.Operation.Target || decision.Rule != rule.Definition.Rule
            || context.Entry(effect).State != EntityState.Added
            || context.ModerationDecisions.Local.SingleOrDefault(row => row.Id == decision.Id) != decision
            || !await CurrentAsync(acquired, token))
            return null;

        // Native humans/roles precede all subordinate locks. Discovery was performed by the
        // human or maintenance authority helper; a replacement approver is never locked here.
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.disclosure:{acquired.Source.Binding.Id:N}:{effect.Id:N}", token);
        var discovered = await context.ModerationNoticeDisclosureApprovals.AsNoTracking()
            .SingleOrDefaultAsync(row => row.EffectId == effect.Id, token);
        if (discovered is null || !acquired.Source.FencedHumans.Contains(discovered.ApprovedByHumanId)
            || !acquired.Source.Administrators.Contains(discovered.ApprovedByHumanId))
            return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationNoticeDisclosureApproval>(context, discovered.Id, token);
        var approval = await context.ModerationNoticeDisclosureApprovals.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == discovered.Id, token);
        if (approval is null) return null;
        approval.ValidatePersistedState();
        if (approval.ApprovedByHumanId != discovered.ApprovedByHumanId
            || approval.HostBindingId != acquired.Source.Binding.Id
            || approval.TrustDomainId != effect.TrustDomainId || approval.TenantId != acquired.TenantId
            || approval.EffectId != effect.Id || approval.DecisionId != decision.Id
            || approval.DecisionRevision != decision.Revision || approval.CaseId != moderationCase.Id
            || approval.CaseRevision != moderationCase.Revision || approval.PolicyId != publication.PolicyId
            || approval.PolicyRevision != publication.Revision || approval.PublicationId != publication.Id
            || approval.ProfileId != rule.Definition.Id || approval.RevokedAt is not null
            || approval.ApprovedAt > observedAt || approval.ValidUntil <= observedAt
            || approval.Intent.ContentRepresentatives.Any(row => !acquired.Source.FencedHumans.Contains(row.HumanId))
            || !await context.Users.AsNoTracking().AnyAsync(
                row => row.Id == approval.ApprovedByHumanId && !row.IsDeleted, token))
            return null;
        var receiptId = await context.ModerationQualificationMutationReceipts.AsNoTracking()
            .Where(row => row.SourceId == approval.Id
                && row.SourceKind == ModerationQualificationSourceKind.NoticeDisclosure && row.ResultRevision == 1)
            .Select(row => (Guid?)row.Id).SingleOrDefaultAsync(token);
        if (receiptId is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationMutationReceipt>(context, receiptId.Value, token);
        var receipt = await context.ModerationQualificationMutationReceipts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == receiptId.Value, token);
        if (receipt is null) return null;
        receipt.ValidatePersistedState();
        var accepted = new ApproveModerationNoticeDisclosureSource
        {
            OperationId = receipt.Id,
            Target = decision.Scope,
            ExpectedQualificationRevision = approval.WriterGeneration - 1,
            Intent = approval.Intent
        };
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            accepted.GetType().FullName + "\n" + JsonSerializer.Serialize(accepted))));
        if (receipt.RequestDigest != digest || receipt.SourceId != approval.Id
            || receipt.SourceKind != ModerationQualificationSourceKind.NoticeDisclosure || receipt.ResultRevision != approval.Revision
            || receipt.HostBindingId != approval.HostBindingId || receipt.TrustDomainId != approval.TrustDomainId
            || receipt.TenantId != approval.TenantId || receipt.PrincipalId != approval.ApprovedByHumanId
            || receipt.QualificationRevision != approval.WriterGeneration || receipt.CompletedAt != approval.ApprovedAt
            || receipt.QualificationRevision > grant.QualificationRevision)
            return null;

        var reason = acquired.Policy.Proposed.Reasons.Single(row => row.Reference == rule.Definition.Reason);
        var classification = acquired.Policy.Proposed.Classes.Single(row => row.Reference == reason.Class);
        if (approval.RuleMeaning != classification.Meaning || approval.ReasonMeaning != reason.Meaning)
            return null;
        var procedural = await activation.AcquireQualificationPolicyAsync(
            acquired.Source.Binding.Id, moderationCase, observedAt, token);
        if (procedural is null) return null;
        var current = procedural.Current;
        var adopted = procedural.Approval;
        var reviewers = new ModerationNoticeReviewerQualification(moderationCase.Id, decision.Scope,
            new(adopted.ApprovedSourceId, adopted.ApprovedSourceRevision, adopted.ApprovedWriterGeneration),
            new(current.SourceId, current.SourceRevision, current.WriterGeneration),
            current.MaximumReviewerLifetime, observedAt, procedural.ValidUntil);
        var content = new ModerationNoticeContentApproval(approval.Id, approval.Revision, effect.Id,
            decision.Id, decision.Revision, decision.Subject, decision.Scope,
            new(publication.Id, publication.Revision, publication.WriterGeneration),
            approval.ProfileId, approval.ContributionId, approval.SelectionDigest,
            new(approval.RuleMeaning), new(approval.ReasonMeaning), approval.Intent.Facts,
            new(approval.RemainingActions), new(approval.Recourse), new(approval.Automation), new(approval.Retention),
            approval.ApprovedAt, approval.ValidUntil, approval.RetainUntil)
        {
            ContentRepresentatives = approval.Intent.ContentRepresentatives
        };
        var until = approval.ValidUntil < procedural.ValidUntil ? approval.ValidUntil : procedural.ValidUntil;
        var completedAt = clock.GetUtcNow();
        return completedAt >= observedAt && completedAt < until && await CurrentAsync(acquired, token)
            ? new(content, reviewers, until) : null;
    }
}
