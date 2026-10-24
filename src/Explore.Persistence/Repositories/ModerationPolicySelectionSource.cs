using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

/// <summary>Concrete persisted-profile source shared by activation and immutable notice capture.</summary>
public sealed class ModerationPolicySelectionSource(
    ExploreDbContext context, IModerationPolicyPublicationRepository publications, ModerationCapabilityRegistry inventory)
    : IModerationPolicyPublicationSelectionSource
{
    public async Task<ModerationPolicyPublicationSelection?> AcquireAsync(
        Guid transactionId, Guid hostBindingId, AuthorityScope target, ModerationRuleReference rule,
        long expectedPolicyRevision, bool activeOnly, CancellationToken cancellationToken)
    {
        AuthorityScope? jurisdiction = target;
        while (jurisdiction is not null && jurisdiction.Id != rule.OwnerId) jurisdiction = jurisdiction.Parent;
        if (jurisdiction is null || jurisdiction.Kind is not (ModerationScopeKind.Instance or ModerationScopeKind.Tenant))
            return null;
        // Discovery is not authority; AcquireSelectedAsync rereads under the jurisdiction slot fence.
        var policyId = await context.ModerationPolicySelections.AsNoTracking()
            .Where(row => row.HostBindingId == hostBindingId && row.JurisdictionId == jurisdiction.Id)
            .Select(row => (Guid?)row.Id).SingleOrDefaultAsync(cancellationToken);
        if (policyId is null) return null;
        var publication = await publications.AcquireSelectedAsync(
            transactionId, hostBindingId, jurisdiction, policyId.Value, activeOnly, cancellationToken);
        if (publication is null || publication.Revision != expectedPolicyRevision) return null;
        var draft = activeOnly ? ModerationPolicyPublicationProjection.Restore(publication, inventory)
            : ModerationPolicyPublicationProjection.RestoreHistory(publication);
        var profile = draft?.Profiles.SingleOrDefault(row => row.Definition.Rule == rule);
        if (draft is null || profile is null || activeOnly && !draft.Proposed.SupportedProfiles.Contains(rule)
            || !profile.Definition.ScopeKinds.Contains(target.Kind)
            || !profile.Reference.AcceptsScope(publication.Jurisdiction, target))
            return null;
        return new(publication, draft, profile,
            new(publication.Id, publication.Revision, publication.WriterGeneration));
    }
}

/// <summary>
/// Uses real stored meanings/plans and a separate native approved-disclosure authority. This class
/// supplies the profile half of notice capture; it cannot run without that substantive owner.
/// It never reads custody justification and never upgrades ordinary policy text to safe text.
/// </summary>
public sealed class ModerationPolicySelectionNoticeSourceOwner(
    ExploreDbContext context, IModerationPolicyPublicationSelectionSource selections,
    IModerationPolicyPublicationNoticeAuthority disclosure, TimeProvider clock) : IModerationNoticeSourceOwner
{
    public async Task<ModerationNoticeSourceObservation?> AcquireAsync(
        Guid transactionId, ModerationEffectIntent effect, ModerationDecision decision,
        ModerationSanction? sanction, ModerationAuthorityGrant grant,
        DateTimeOffset observedAt, CancellationToken cancellationToken)
    {
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (host is null || effect.PolicyRevision != grant.PolicyRevision
            || effect.DecisionId != decision.Id || effect.DecisionRevision != decision.Revision
            || effect.TrustDomainId != decision.Scope.TrustDomainId || decision.Subject != grant.Subject
            || !grant.Scope.Contains(decision.Scope))
            return null;
        var selected = await selections.AcquireAsync(transactionId, host.Id, decision.Scope, decision.Rule,
            effect.PolicyRevision, false, cancellationToken)
            ?? await selections.AcquireAsync(transactionId, host.Id, decision.Scope, decision.Rule,
                effect.PolicyRevision, true, cancellationToken);
        if (selected is null) return null;
        var approved = await disclosure.AcquireAsync(transactionId, selected.Publication, effect, decision,
            sanction, grant, observedAt, cancellationToken);
        if (approved is null || approved.TransactionId != transactionId || approved.ObservedAt != observedAt
            || approved.ValidUntil <= observedAt || context.Database.CurrentTransaction?.TransactionId != transactionId)
            return null;
        var approval = approved.Approval;
        var contribution = approved.Contribution;
        var reviewers = approved.ReviewerQualification;
        var definition = selected.Profile.Definition;
        var reason = selected.Draft.Proposed.Reasons.Single(row => row.Reference == definition.Reason);
        var classification = selected.Draft.Proposed.Classes.Single(row => row.Reference == reason.Class);
        if (approval.ApprovedProfileSource != selected.Source
            || approval.ProfileId != definition.Id || approval.EffectId != effect.Id
            || approval.DecisionId != decision.Id || approval.DecisionRevision != decision.Revision
            || approval.Subject != decision.Subject || approval.Scope != decision.Scope
            || approval.ContributionId != contribution.Id || contribution.Rule != definition.Rule
            || approval.SelectionDigest != ModerationNoticeSnapshotFactory.SelectionDigest(contribution)
            || approval.RuleMeaning?.Value != classification.Meaning || approval.ReasonMeaning?.Value != reason.Meaning
            || approval.ApprovedAt > observedAt || approval.ValidUntil <= observedAt
            || contribution.Scope != decision.Scope || contribution.SubjectKind != decision.Subject.Kind
            || contribution.SubjectId != decision.Subject.Id || contribution.StartsAt != decision.StartsAt
            || reviewers is null || reviewers.CaseId != decision.CaseId || reviewers.Scope != decision.Scope
            || reviewers.ApprovedSource != reviewers.CurrentSource || reviewers.MaximumLifetime <= TimeSpan.Zero
            || reviewers.ObservedAt != observedAt || reviewers.ValidUntil <= observedAt)
            return null;
        var composed = ModerationPolicyProposedStateCompiler.ComposeRetained(selected.Draft, [contribution], observedAt);
        var targets = composed.Effects.Select(row => row.Target).Concat(composed.Consequences.Select(row => row.Target)).Distinct().ToArray();
        if (composed.Outcome != ModerationPolicyCompositionOutcome.Preview || composed.Consequences.IsEmpty
            || composed.Effects.Any(row => row.Interval.EndsAt != decision.EndsAt || row.Interval.ReviewAt != decision.ReviewAt)
            || approved.Targets.IsDefault || approved.Targets.Length != targets.Length
            || approved.Targets.Select(row => row.Target).Distinct().Count() != targets.Length
            || approved.Targets.Any(row => !targets.Contains(row.Target) || row.ApprovedSource != row.CurrentSource
                || row.ObservedAt != observedAt || row.ValidUntil <= observedAt))
            return null;
        // Detect boundary tampering during disclosure acquisition with another actual fenced read.
        var repeated = await selections.AcquireAsync(transactionId, host.Id, decision.Scope, decision.Rule,
            effect.PolicyRevision, false, cancellationToken)
            ?? await selections.AcquireAsync(transactionId, host.Id, decision.Scope, decision.Rule,
                effect.PolicyRevision, true, cancellationToken);
        var now = clock.GetUtcNow();
        return repeated?.Source == selected.Source && now >= observedAt
            && now < approved.ValidUntil && now < approval.ValidUntil && now < reviewers.ValidUntil
            && approved.Targets.All(row => now < row.ValidUntil)
            ? new(transactionId, observedAt, approved.ValidUntil, approval, selected.Source, selected.Draft,
                contribution, approved.Targets, reviewers) : null;
    }
}
