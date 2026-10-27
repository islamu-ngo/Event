using System.Collections.Immutable;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Contracts.Notifications;

public interface IModerationNoticeCapture
{
    /// <summary>
    /// Called after the effect and operation receipt are staged, before commit, under the writer's
    /// existing fences. Null means required source authority is missing; the writer must roll back,
    /// including any shared-context flush. A retained inert transition requires no disclosure owner.
    /// The writer must enforce the returned snapshot's acquisition deadline through its final flush.
    /// The originating session identifies the actual acquisition; grant coordinates alone do not.
    /// Never call from an asynchronous effect planner to reconstruct an original explanation.
    /// </summary>
    Task<ModerationNoticeSource?> CaptureInCurrentTransactionAsync(
        IModerationSession session, Guid effectId, ModerationAuthorityGrant grant, CancellationToken cancellationToken);
}

/// <summary>
/// Versioned writer capture stored in ModerationNoticeSource.SnapshotJson. An inert decision has no
/// finding, approval or sanction and cannot select substantive consequences. Notice is then null,
/// not an empty approved disclosure. A notice retains all original effects and consequence plans.
/// Pending corrections retain substantive obligations without an approved body or delivery grant.
/// None of these shapes is a completion receipt. Host coordinates are immutable; there is no host revision.
/// </summary>
public sealed record ModerationNoticeCaptureSnapshot(
    int Version, string WriterGeneration, Guid HostBindingId, Guid HostSolutionId, Guid HostInstanceId,
    Guid WriterTenantId, Guid AuthoritySourceId, long QualificationRevision,
    ModerationNoticeOriginalDecision Decision, ModerationNoticeOriginalFinding? Finding,
    ModerationNoticeSnapshot? Notice, ImmutableArray<ModerationNoticeReviewerProof> Reviewers,
    DateTimeOffset? ValidUntil, ModerationNoticeSourceStamp? ReviewerPolicySource = null)
{
    public ImmutableArray<ModerationNoticeContentRepresentative> ContentRepresentatives { get; init; } = [];

    public ModerationNoticeCorrectionObligations? PendingCorrection { get; init; }

    public DateTimeOffset? RetainUntil => Notice?.RetainUntil ?? PendingCorrection?.RetainUntil;

    public bool HasNoSubstantiveWork => Decision is not null && Notice is null && Finding is null
        && PendingCorrection is null
        && Decision.FindingId is null && Decision.ApprovalCount == 0
        && Decision.SanctionId is null && Decision.SupersededSanctionId is null
        && Decision.State is ModerationDecisionState.Proposed or ModerationDecisionState.AwaitingFinding
            or ModerationDecisionState.Rejected or ModerationDecisionState.Withdrawn
        && !Reviewers.IsDefault && Reviewers.IsEmpty && ValidUntil is null && ReviewerPolicySource is null
        && !ContentRepresentatives.IsDefault && ContentRepresentatives.IsEmpty;

    public bool Matches(ModerationEffectIntent effect) =>
        Version == 2 && WriterGeneration == "event.moderation.capture.v2"
        && HostBindingId != Guid.Empty && HostSolutionId != Guid.Empty && HostInstanceId != Guid.Empty
        && WriterTenantId != Guid.Empty && AuthoritySourceId != Guid.Empty
        && QualificationRevision == effect.QualificationRevision
        && Decision is not null && Decision.Id == effect.DecisionId && Decision.Revision == effect.DecisionRevision
        && Decision.Scope is not null
        && Decision.Scope.Kind == (ModerationScopeKind)effect.ScopeKind && Decision.Scope.Id == effect.ScopeId
        && Decision.SubjectKind == (ModerationSubjectKind)effect.SubjectKind && Decision.SubjectId == effect.SubjectId
        && Decision.SanctionId == effect.SanctionId && Decision.SupersededSanctionId == effect.SupersededSanctionId
        && (HasNoSubstantiveWork
            || PendingCorrection is { } pending && MatchesPendingCorrection(effect, pending)
            || PendingCorrection is null && Notice is { Version: 1 } notice
            && Finding is not null && Finding.Id == Decision.FindingId && Finding.Reason == notice.Reason
            && notice.EffectId == effect.Id && notice.OperationId == effect.OperationId
            && notice.DecisionId == effect.DecisionId && notice.DecisionRevision == effect.DecisionRevision
            && notice.PolicyRevision == effect.PolicyRevision && notice.CapturedAt == effect.CreatedAt
            && notice.SanctionId == effect.SanctionId && notice.SanctionRevision == effect.SanctionRevision
            && notice.SupersededSanctionId == effect.SupersededSanctionId
            && notice.SupersededSanctionRevision == effect.SupersededSanctionRevision
            && !notice.Consequences.IsDefaultOrEmpty && !Reviewers.IsDefaultOrEmpty && ReviewerPolicySource is not null
            && !ContentRepresentatives.IsDefault && ContentRepresentatives.Length <= 32
            && (Decision.SubjectKind == ModerationSubjectKind.Content
                ? !ContentRepresentatives.IsEmpty : ContentRepresentatives.IsEmpty)
            && ContentRepresentatives.All(row => row is not null && row.AssignmentId != Guid.Empty
                && row.AssignmentId.Version == 7 && row.AssignmentId.Variant is >= 8 and <= 11
                && row.AssignmentRevision is >= 1 and < long.MaxValue && row.HumanId != Guid.Empty)
            && ContentRepresentatives.Select(row => row.AssignmentId).Distinct().Count() == ContentRepresentatives.Length
            && ContentRepresentatives.Select(row => row.HumanId).Distinct().Count() == ContentRepresentatives.Length
            && ValidUntil > effect.CreatedAt && ModerationNoticeRetention.CanNotifyAt(
                effect.CreatedAt, notice.ContestationCalendarMonths, notice.RetainUntil));

    private bool MatchesPendingCorrection(ModerationEffectIntent effect, ModerationNoticeCorrectionObligations pending) =>
        effect.OperationKind == (int)ModerationOperationKind.Revoke
        && effect.SanctionId is not null && effect.SanctionRevision >= 2
        && Decision.SanctionId == effect.SanctionId
        && Notice is null && Finding is null
        && !Reviewers.IsDefault && Reviewers.IsEmpty && ReviewerPolicySource is null
        && !ContentRepresentatives.IsDefault && ContentRepresentatives.IsEmpty
        && ValidUntil is { Offset: var validOffset } && validOffset == TimeSpan.Zero
        && ValidUntil == pending.RetainUntil
        && pending.RetainUntil.Offset == TimeSpan.Zero && pending.RetainUntil != DateTimeOffset.MaxValue
        && pending.RetainUntil > effect.CreatedAt
        && IsUuidV7(pending.OriginalEffectId) && pending.OriginalEffectId != effect.Id
        && IsHash(pending.OriginalSnapshotHash) && IsHash(pending.OriginalEffectBindingHash)
        && !pending.Recipients.IsDefaultOrEmpty
        && pending.Recipients.All(row => row is not null && IsUuidV7(row.OriginalNoticeId)
            && Enum.IsDefined(row.Audience) && Enum.IsDefined(row.Disposition)
            && row.Disposition == ModerationNoticeDeliveryDisposition.RecipientUnavailable
            && row.RecipientSubjectKind is >= 1 and <= 4 && row.RecipientSubjectId != Guid.Empty
            && (row.Audience == ModerationNoticeAudience.Subject
                ? row.RecipientSubjectKind == effect.SubjectKind && row.RecipientSubjectId == effect.SubjectId
                : row.RecipientSubjectKind == (int)ModerationSubjectKind.Human)
            && (row.Audience != ModerationNoticeAudience.Reviewer || row.RecipientSubjectId != effect.SubjectId))
        && pending.Recipients.Count(row => row.Audience == ModerationNoticeAudience.Subject) == 1
        && pending.Recipients.Select(row => row.OriginalNoticeId).Distinct().Count() == pending.Recipients.Length
        && pending.Recipients.Select(row => (row.Audience, row.RecipientSubjectKind, row.RecipientSubjectId))
            .Distinct().Count() == pending.Recipients.Length;

    private static bool IsUuidV7(Guid value) =>
        value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11;

    private static bool IsHash(string? value) =>
        value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower);

    public override string ToString() => nameof(ModerationNoticeCaptureSnapshot);
}

/// <summary>
/// Unresolved correction metadata, not disclosure authority or evidence of delivery. The native owner
/// must verify the exact creating Activate/Supersede source, both hashes and the full original occurrence
/// set. RetainUntil must be inherited from that unexpired source; this shape cannot prove authenticity
/// or authorize a new lifetime, historical access, a notice body or native notification work.
/// </summary>
public sealed record ModerationNoticeCorrectionObligations(
    Guid OriginalEffectId, string OriginalSnapshotHash, string OriginalEffectBindingHash,
    DateTimeOffset RetainUntil, ImmutableArray<ModerationNoticeCorrectionRecipient> Recipients)
{
    public override string ToString() => nameof(ModerationNoticeCorrectionObligations);
}

/// <summary>An original recipient occurrence still owed correction, never a current case-reading grant.</summary>
public sealed record ModerationNoticeCorrectionRecipient(
    Guid OriginalNoticeId, ModerationNoticeAudience Audience, int RecipientSubjectKind,
    Guid RecipientSubjectId, ModerationNoticeDeliveryDisposition Disposition)
{
    public override string ToString() => nameof(ModerationNoticeCorrectionRecipient);
}

public sealed record ModerationNoticeOriginalDecision(
    Guid Id, long Revision, Guid CaseId, ModerationDecisionState State,
    ModerationNoticeScope Scope, ModerationSubjectKind SubjectKind, Guid SubjectId,
    ModerationNoticeScope SubjectOrigin, ModerationNoticeRule Rule, int CapabilityMask,
    DateTimeOffset StartsAt, DateTimeOffset? EndsAt, DateTimeOffset? ReviewAt,
    Guid? FindingId, int ApprovalCount, Guid? SanctionId, Guid? SupersededSanctionId);

/// <summary>Original cause identity and reason, never finding justification or custody evidence.</summary>
public sealed record ModerationNoticeOriginalFinding(
    Guid Id, long Revision, ModerationFindingState State, ModerationNoticeRule Reason);

public sealed record ModerationNoticeReviewerProof(
    Guid AssignmentId, long Revision, Guid ReviewerHumanId, Guid ControllerHumanId,
    Guid IdentityEvidenceId, long IdentityEvidenceRevision, Guid QualifiedByHumanId,
    Guid ConflictEvidenceId, long ConflictEvidenceRevision,
    Guid AssessedByHumanId, DateTimeOffset QualifiedUntil);

/// <summary>
/// The native safe-composition/profile owner, not an HTTP payload adapter. It must acquire the
/// actual approval, adopted profile and target-owner fences on the supplied primary transaction,
/// independently reread their current sources, and retain those fences through commit.
/// No default implementation, catalogue-only approval or authored-test conformance is permitted.
/// </summary>
public interface IModerationNoticeSourceOwner
{
    Task<ModerationNoticeSourceObservation?> AcquireAsync(
        Guid transactionId, ModerationEffectIntent effect, ModerationDecision decision,
        ModerationSanction? sanction, ModerationAuthorityGrant grant,
        DateTimeOffset observedAt, CancellationToken cancellationToken);
}

public sealed record ModerationNoticeSourceStamp(Guid Id, long Revision, string WriterGeneration);

/// <summary>
/// A separately approved subject disclosure. Facts are deliberately distinct from finding
/// justification and evidence. Ordinary adopted meanings are usable only if this approval
/// explicitly approved those exact strings for this effect and audience.
/// </summary>
public sealed record ModerationNoticeContentApproval(
    Guid Id, long Revision, Guid EffectId, Guid DecisionId, long DecisionRevision,
    SubjectReference Subject, AuthorityScope Scope,
    ModerationNoticeSourceStamp ApprovedProfileSource,
    Guid ProfileId, Guid ContributionId, string SelectionDigest,
    ModerationNoticeSafeText RuleMeaning, ModerationNoticeSafeText ReasonMeaning,
    ImmutableArray<ModerationNoticeSafeText> Facts,
    ModerationNoticeSafeText RemainingActions, ModerationNoticeSafeText Recourse,
    ModerationNoticeSafeText Automation, ModerationNoticeSafeText Retention,
    DateTimeOffset ApprovedAt, DateTimeOffset ValidUntil, DateTimeOffset RetainUntil)
{
    public ImmutableArray<ModerationNoticeContentRepresentative> ContentRepresentatives { get; init; } = [];

    public override string ToString() => nameof(ModerationNoticeContentApproval);
}

/// <summary>Both coordinates come from independent reads, not a copied approval or target mask.</summary>
public sealed record ModerationNoticeTargetObservation(
    ModerationPolicyTarget Target, string Owner,
    ModerationNoticeSourceStamp ApprovedSource, ModerationNoticeSourceStamp CurrentSource,
    DateTimeOffset ObservedAt, DateTimeOffset ValidUntil);

/// <summary>
/// Independently approved current reviewer qualification policy for this exact case and scope.
/// Authoring duration ceilings are not operational reviewer qualification limits.
/// </summary>
public sealed record ModerationNoticeReviewerQualification(
    Guid CaseId, AuthorityScope Scope,
    ModerationNoticeSourceStamp ApprovedSource, ModerationNoticeSourceStamp CurrentSource,
    TimeSpan MaximumLifetime, DateTimeOffset ObservedAt, DateTimeOffset ValidUntil);

public sealed record ModerationNoticeSourceObservation(
    Guid TransactionId, DateTimeOffset ObservedAt, DateTimeOffset ValidUntil,
    ModerationNoticeContentApproval Approval,
    ModerationNoticeSourceStamp CurrentProfileSource,
    ModerationPolicyCompiledDraft AdoptedProfile,
    ModerationPolicyContribution SelectedContribution,
    ImmutableArray<ModerationNoticeTargetObservation> Targets,
    ModerationNoticeReviewerQualification ReviewerQualification)
{
    public override string ToString() => nameof(ModerationNoticeSourceObservation);
}
