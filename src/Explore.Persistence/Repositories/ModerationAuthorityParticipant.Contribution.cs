using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

/// <summary>Receipt-bound authored draft and disclosure; native target adoption is still independent.</summary>
public sealed record ModerationNoticeNativeContributionDraft(
    ModerationNoticeNativeQualification Notice, ModerationPolicyContribution Contribution,
    Guid SelectionId, long SelectionRevision, long WriterGeneration, DateTimeOffset ValidUntil)
{
    public override string ToString() => nameof(ModerationNoticeNativeContributionDraft);
}

public sealed partial class ModerationAuthorityParticipant
{
    /// <summary>
    /// Joins the retained complete draft to separately approved disclosure on the acquired
    /// lifecycle transaction. No draft target coordinate becomes a native target observation.
    /// </summary>
    public async Task<ModerationNoticeNativeContributionDraft?> AcquireNoticeContributionDraftAsync(
        Guid transactionId, ModerationPolicyPublication publication, ModerationEffectIntent effect,
        ModerationDecision decision, ModerationAuthorityGrant grant, DateTimeOffset observedAt,
        CancellationToken token)
    {
        var notice = await AcquireNoticeQualificationAsync(
            transactionId, publication, effect, decision, grant, observedAt, token);
        var acquired = _acquisition;
        if (notice is null || acquired is null) return null;

        // The author was discovered before native human/role fences. Never introduce a new
        // identity lock while holding the case, publication or disclosure slot.
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.contribution:{acquired.Source.Binding.Id:N}:{effect.Id:N}", token);
        var discovered = await context.ModerationContributionSelections.AsNoTracking()
            .SingleOrDefaultAsync(row => row.EffectId == effect.Id, token);
        if (discovered is null || !acquired.Source.FencedHumans.Contains(discovered.AuthoredByHumanId)
            || !acquired.Source.Administrators.Contains(discovered.AuthoredByHumanId))
            return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationContributionSelection>(context, discovered.Id, token);
        var selection = await context.ModerationContributionSelections.AsNoTracking().Include(row => row.Bindings)
            .SingleOrDefaultAsync(row => row.Id == discovered.Id, token);
        if (selection is null) return null;
        selection.ValidatePersistedState();
        if (selection.AuthoredByHumanId != discovered.AuthoredByHumanId
            || selection.HostBindingId != acquired.Source.Binding.Id
            || selection.TrustDomainId != effect.TrustDomainId || selection.TenantId != acquired.TenantId
            || selection.EffectId != effect.Id || selection.DecisionId != decision.Id
            || selection.DecisionRevision != decision.Revision || selection.CaseId != decision.CaseId
            || selection.CaseRevision != acquired.Case!.Revision || selection.PolicyId != publication.PolicyId
            || selection.PolicyRevision != publication.Revision || selection.PublicationId != publication.Id
            || selection.ProfileId != acquired.Rule!.Definition.Id
            || selection.DecisionId != notice.Content.ContributionId || selection.SelectionDigest != notice.Content.SelectionDigest
            || selection.StartsAt != decision.StartsAt || selection.RecordedAt > observedAt
            || selection.ValidUntil <= observedAt || selection.WriterGeneration > grant.QualificationRevision
            || !await context.Users.AsNoTracking().AnyAsync(
                row => row.Id == selection.AuthoredByHumanId && !row.IsDeleted, token))
            return null;

        var contribution = ModerationContributionSelectionProjection.Restore(selection, decision);
        if (ModerationNoticeSnapshotFactory.SelectionDigest(contribution) != selection.SelectionDigest)
            return null;
        var receiptId = await context.ModerationQualificationMutationReceipts.AsNoTracking()
            .Where(row => row.SourceId == selection.Id
                && row.SourceKind == ModerationQualificationSourceKind.ContributionSelection && row.ResultRevision == 1)
            .Select(row => (Guid?)row.Id).SingleOrDefaultAsync(token);
        if (receiptId is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationMutationReceipt>(context, receiptId.Value, token);
        var receipt = await context.ModerationQualificationMutationReceipts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == receiptId.Value, token);
        if (receipt is null) return null;
        receipt.ValidatePersistedState();
        var accepted = new RecordModerationContributionSelectionSource
        {
            OperationId = receipt.Id,
            Target = decision.Scope,
            ExpectedQualificationRevision = selection.WriterGeneration - 1,
            SelectionId = selection.Id,
            EffectId = selection.EffectId,
            CaseId = selection.CaseId,
            ExpectedCaseRevision = selection.CaseRevision,
            DecisionId = selection.DecisionId,
            ExpectedDecisionRevision = selection.DecisionRevision,
            PolicyId = selection.PolicyId,
            ExpectedPolicyRevision = selection.PolicyRevision,
            PublicationId = selection.PublicationId,
            ProfileId = selection.ProfileId,
            Contribution = contribution,
            ValidUntil = selection.ValidUntil
        };
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            accepted.GetType().FullName + "\n" + JsonSerializer.Serialize(accepted))));
        if (receipt.RequestDigest != digest || receipt.SourceId != selection.Id
            || receipt.SourceKind != ModerationQualificationSourceKind.ContributionSelection
            || receipt.ResultRevision != selection.Revision || receipt.HostBindingId != selection.HostBindingId
            || receipt.TrustDomainId != selection.TrustDomainId || receipt.TenantId != selection.TenantId
            || receipt.PrincipalId != selection.AuthoredByHumanId
            || receipt.QualificationRevision != selection.WriterGeneration || receipt.CompletedAt != selection.RecordedAt)
            return null;
        var composed = ModerationPolicyProposedStateCompiler.ComposeRetained(acquired.Policy, [contribution], observedAt);
        if (composed.Outcome != ModerationPolicyCompositionOutcome.Preview || composed.Consequences.IsEmpty
            || composed.Effects.Any(row => row.Interval.EndsAt != decision.EndsAt || row.Interval.ReviewAt != decision.ReviewAt)
            || composed.Consequences.Any(row => row.Interval.EndsAt != decision.EndsAt || row.Interval.ReviewAt != decision.ReviewAt))
            return null;
        var until = notice.ValidUntil < selection.ValidUntil ? notice.ValidUntil : selection.ValidUntil;
        var completedAt = clock.GetUtcNow();
        return completedAt >= observedAt && completedAt < until && await CurrentAsync(acquired, token)
            ? new(notice, contribution, selection.Id, selection.Revision, selection.WriterGeneration, until) : null;
    }
}
