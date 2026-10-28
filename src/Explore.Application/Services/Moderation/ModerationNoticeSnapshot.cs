using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Domain;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Explore.Application.Services.Moderation;

/// <summary>Serializable ancestry; unlike AuthorityScope, it has no private construction contract.</summary>
public sealed record ModerationNoticeScope(
    ModerationScopeKind Kind, Guid Id, ModerationNoticeScope? Parent)
{
    public static ModerationNoticeScope From(AuthorityScope scope) =>
        new(scope.Kind, scope.Id, scope.Parent is null ? null : From(scope.Parent));
}

public sealed record ModerationNoticeRule(Guid OwnerId, string Code, long Revision);

public sealed record ModerationNoticeTarget(
    ModerationNoticeScope Scope, ModerationSubjectKind SubjectKind, Guid SubjectId,
    Guid TargetId, long TargetRevision, string Operation, DateTimeOffset? WindowEnd,
    string Owner, ModerationNoticeSourceStamp Source);

public sealed record ModerationNoticeEffect(
    ModerationReferenceEffect Effect, ModerationNoticeTarget Target, ModerationReferenceInterval Interval);

public sealed record ModerationNoticeConsequence(
    ModerationAdditiveStrategyKind? Strategy, ModerationProtectionPurpose? ProtectionPurpose,
    ModerationNoticeTarget Target, ModerationReferenceInterval Interval, ModerationConsequenceModes Plan);

/// <summary>
/// An explanation of one committed transition, not today's effective-rights projection.
/// Consequences remain selected work; neither this snapshot nor notice delivery marks them complete.
/// </summary>
public sealed record ModerationNoticeSnapshot(
    int Version, Guid EffectId, Guid OperationId, Guid DecisionId, long DecisionRevision,
    ModerationDecisionState DecisionState, Guid? SanctionId, long? SanctionRevision,
    ModerationSanctionState? SanctionState, Guid? SupersededSanctionId, long? SupersededSanctionRevision,
    int OperationKind, ModerationNoticeScope Scope,
    ModerationSubjectKind SubjectKind, Guid SubjectId, ModerationNoticeScope SubjectOrigin,
    Guid PolicyId, long PolicyRevision, Guid PublicationId, long CatalogueRevision,
    Guid ProfileId, ModerationReferenceProfileId ReferenceProfile, long ReferenceRevision,
    ModerationNoticeSourceStamp ProfileSource, Guid ApprovalId, long ApprovalRevision,
    Guid ContributionId, ModerationNoticeRule Rule, ModerationNoticeRule Reason,
    ModerationNoticeSafeText RuleMeaning, ModerationNoticeSafeText ReasonMeaning,
    ImmutableArray<ModerationNoticeSafeText> Facts, ModerationNoticeSafeText RemainingActions,
    ModerationNoticeSafeText Recourse, ModerationNoticeSafeText Automation, ModerationNoticeSafeText Retention,
    ImmutableArray<ModerationNoticeEffect> Effects, ImmutableArray<ModerationNoticeConsequence> Consequences,
    DateTimeOffset CapturedAt, DateTimeOffset RetainUntil, int ContestationCalendarMonths)
{
    public override string ToString() => nameof(ModerationNoticeSnapshot);
}

/// <summary>
/// Structural qualification after native source acquisition. The compiler provides exact plans;
/// the source owner proves adoption and disclosure. Neither substitutes for the other.
/// </summary>
public static class ModerationNoticeSnapshotFactory
{
    public static ModerationNoticeSnapshot? Create(
        Guid transactionId, ModerationEffectIntent effect, ModerationDecision decision,
        ModerationSanction? sanction, ModerationPolicyCompiledDraft selected,
        ModerationNoticeSourceObservation observation, DateTimeOffset observedAt)
    {
        var approval = observation.Approval;
        var draft = observation.AdoptedProfile;
        var contribution = observation.SelectedContribution;
        var reviewers = observation.ReviewerQualification;
        if (transactionId == Guid.Empty || observation.TransactionId != transactionId
            || observation.ObservedAt != observedAt || observation.ValidUntil <= observedAt
            || !Instant(observation.ValidUntil)
            || !Instant(observedAt) || observedAt < effect.CreatedAt
            || approval is null || draft is null || contribution is null
            || !Version7(approval.Id) || approval.Revision < 1
            || approval.ApprovedAt > observedAt || approval.ValidUntil <= observedAt
            || !Instant(approval.ApprovedAt) || !Instant(approval.ValidUntil)
            || approval.EffectId != effect.Id || approval.DecisionId != decision.Id
            || approval.DecisionRevision != decision.Revision
            || approval.Subject != decision.Subject || approval.Scope != decision.Scope
            || effect.DecisionId != decision.Id || effect.DecisionRevision != decision.Revision
            || effect.TrustDomainId != decision.Scope.TrustDomainId
            || effect.ScopeKind != (int)decision.Scope.Kind || effect.ScopeId != decision.Scope.Id
            || effect.SubjectKind != (int)decision.Subject.Kind || effect.SubjectId != decision.Subject.Id
            || effect.CompletedAt is not null
            || effect.SanctionId != sanction?.Id || effect.SanctionRevision != sanction?.Revision
            || sanction is not null && (sanction.DecisionId != decision.Id
                || sanction.Subject != decision.Subject || sanction.Scope != decision.Scope
                || sanction.Rule != decision.Rule || sanction.StartsAt != decision.StartsAt
                || sanction.EndsAt != decision.EndsAt || sanction.ReviewAt != decision.ReviewAt)
            || !Stamp(observation.CurrentProfileSource)
            || approval.ApprovedProfileSource != observation.CurrentProfileSource
            || draft.Publication != selected.Publication
            || ModerationPolicyPublicationProjection.Digest(draft)
                != ModerationPolicyPublicationProjection.Digest(selected)
            || selected.Publication.ProposedRevision != effect.PolicyRevision
            || observation.CurrentProfileSource.Id != selected.Publication.PublicationId
            || observation.CurrentProfileSource.Revision != selected.Publication.ProposedRevision
            || observation.CurrentProfileSource.WriterGeneration != selected.Publication.WriterGeneration
            || !selected.Publication.Jurisdiction.Contains(decision.Scope)
            || contribution.Id != approval.ContributionId || contribution.Rule != decision.Rule
            || approval.SelectionDigest != SelectionDigest(contribution)
            || contribution.Scope != decision.Scope || contribution.SubjectKind != decision.Subject.Kind
            || contribution.SubjectId != decision.Subject.Id || contribution.StartsAt != decision.StartsAt
            || approval.RuleMeaning is null
            || approval.ReasonMeaning is null || approval.Facts.IsDefaultOrEmpty || approval.Facts.Length > 16
            || approval.Facts.Any(fact => fact is null)
            || approval.RemainingActions is null || approval.Recourse is null
            || approval.Automation is null || approval.Retention is null
            || reviewers is null || reviewers.CaseId != decision.CaseId || reviewers.Scope != decision.Scope
            || !Stamp(reviewers.CurrentSource) || reviewers.ApprovedSource != reviewers.CurrentSource
            || reviewers.MaximumLifetime <= TimeSpan.Zero || reviewers.ObservedAt != observedAt
            || !Instant(reviewers.ValidUntil) || reviewers.ValidUntil <= observedAt)
            return null;

        var profile = draft.Profiles.SingleOrDefault(item => item.Definition.Id == approval.ProfileId);
        if (profile is null || profile.Definition.Rule != decision.Rule)
            return null;
        if (!ModerationNoticeRetention.CanNotifyAt(
            observedAt, profile.Definition.ContestationCalendarMonths, approval.RetainUntil))
            return null;
        var reason = draft.Proposed.Reasons.SingleOrDefault(item => item.Reference == profile.Definition.Reason);
        if (reason is null || approval.ReasonMeaning.Value != reason.Meaning.Trim())
            return null;
        var classification = draft.Proposed.Classes.SingleOrDefault(item => item.Reference == reason.Class);
        if (classification is null || approval.RuleMeaning.Value != classification.Meaning.Trim())
            return null;

        var composed = ModerationPolicyProposedStateCompiler.ComposeRetained(draft, [contribution], observedAt);
        if (composed.Outcome != ModerationPolicyCompositionOutcome.Preview
            || composed.Consequences.IsEmpty
            || composed.Effects.Any(item => item.Interval.StartsAt != decision.StartsAt
                || item.Interval.EndsAt != decision.EndsAt || item.Interval.ReviewAt != decision.ReviewAt)
            || composed.Consequences.Any(item => item.Interval.StartsAt != decision.StartsAt
                || item.Interval.EndsAt != decision.EndsAt || item.Interval.ReviewAt != decision.ReviewAt))
            return null;

        var targets = composed.Effects.Select(item => item.Target)
            .Concat(composed.Consequences.Select(item => item.Target)).Distinct().ToArray();
        if (observation.Targets.IsDefault || observation.Targets.Length != targets.Length
            || observation.Targets.Any(item => item is null || !targets.Contains(item.Target)
                || !Stamp(item.CurrentSource) || item.ApprovedSource != item.CurrentSource
                || item.ObservedAt != observedAt || item.ValidUntil <= observedAt
                || !Instant(item.ValidUntil)
                || !Code(item.Owner))
            || observation.Targets.Select(item => item.Target).Distinct().Count() != targets.Length)
            return null;

        ModerationNoticeTarget Target(ModerationPolicyTarget target)
        {
            var owner = observation.Targets.Single(item => item.Target == target);
            return new(ModerationNoticeScope.From(target.Scope), target.SubjectKind, target.SubjectId,
                target.TargetId, target.TargetRevision, target.Operation, target.WindowEnd,
                owner.Owner, owner.CurrentSource);
        }
        static ModerationNoticeRule Rule(ModerationRuleReference reference) =>
            new(reference.OwnerId, reference.Code, reference.Revision);

        return new(1, effect.Id, effect.OperationId, decision.Id, decision.Revision, decision.State,
            effect.SanctionId, effect.SanctionRevision, sanction?.State,
            effect.SupersededSanctionId, effect.SupersededSanctionRevision, effect.OperationKind,
            ModerationNoticeScope.From(decision.Scope), decision.Subject.Kind, decision.Subject.Id,
            ModerationNoticeScope.From(decision.Subject.Origin), selected.Publication.PolicyId,
            selected.Publication.ProposedRevision, selected.Publication.PublicationId,
            draft.Publication.CatalogueRevision, profile.Definition.Id,
            profile.Definition.ReferenceProfile, profile.Definition.ReferenceRevision,
            observation.CurrentProfileSource, approval.Id, approval.Revision, contribution.Id,
            Rule(decision.Rule), Rule(reason.Reference), approval.RuleMeaning, approval.ReasonMeaning,
            approval.Facts, approval.RemainingActions, approval.Recourse, approval.Automation, approval.Retention,
            composed.Effects.Select(item => new ModerationNoticeEffect(item.Effect, Target(item.Target), item.Interval))
                .ToImmutableArray(),
            composed.Consequences.Select(item => new ModerationNoticeConsequence(item.Strategy,
                item.ProtectionPurpose, Target(item.Target), item.Interval, item.Plan)).ToImmutableArray(),
            effect.CreatedAt, approval.RetainUntil, profile.Definition.ContestationCalendarMonths);
    }

    private static bool Version7(Guid id) => id != Guid.Empty && id.Version == 7 && id.Variant is >= 8 and <= 11;
    /// <summary>Recorded by the disclosure approval owner for the exact selected target/interval input.</summary>
    public static string SelectionDigest(ModerationPolicyContribution contribution) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(contribution)));

    private static bool Instant(DateTimeOffset at) =>
        at.Offset == TimeSpan.Zero && at != DateTimeOffset.MinValue && at != DateTimeOffset.MaxValue;
    private static bool Code(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && value == value.Trim()
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');
    private static bool Stamp(ModerationNoticeSourceStamp source) =>
        source is not null && Version7(source.Id) && source.Revision > 0 && Code(source.WriterGeneration);
}
