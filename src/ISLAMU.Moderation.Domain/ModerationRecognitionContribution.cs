using System.Collections.Frozen;
using ISLAMU.Moderation.Domain.Policies;

namespace ISLAMU.Moderation.Domain;

/// <summary>
/// Minimal purpose-limited contribution coordinates for independently qualified recognition capture.
/// Native capture still requires current policy, final finding, identity and key authority;
/// this snapshot provides neither matching proof nor permission to retain identifiers.
/// </summary>
public sealed class ModerationRecognitionContribution
{
    private ModerationRecognitionContribution(ModerationDecision decision, ModerationSanction sanction,
        ModerationReferenceRecognition purpose, FrozenSet<ModerationCapability> capabilities,
        DateTimeOffset capturedAt, DateTimeOffset retainUntil)
    {
        SanctionId = sanction.Id;
        SanctionRevision = sanction.Revision;
        DecisionId = decision.Id;
        DecisionRevision = decision.Revision;
        OriginalCapabilityMask = sanction.CapabilityMask;
        Rule = sanction.Rule;
        Scope = sanction.Scope;
        Purpose = purpose;
        Capabilities = capabilities;
        StartsAt = sanction.StartsAt;
        EndsAt = sanction.EndsAt;
        ReviewAt = sanction.ReviewAt;
        CapturedAt = capturedAt;
        RetainUntil = retainUntil;
    }

    public Guid SanctionId { get; }
    public long SanctionRevision { get; }
    public Guid DecisionId { get; }
    public long DecisionRevision { get; }
    private int OriginalCapabilityMask { get; }
    public ModerationRuleReference Rule { get; }
    public AuthorityScope Scope { get; }
    public ModerationReferenceRecognition Purpose { get; }
    public IReadOnlySet<ModerationCapability> Capabilities { get; }
    public DateTimeOffset StartsAt { get; }
    public DateTimeOffset? EndsAt { get; }
    public DateTimeOffset? ReviewAt { get; }
    public DateTimeOffset CapturedAt { get; }
    public DateTimeOffset RetainUntil { get; }

    public static ModerationRecognitionContribution Capture(
        ModerationDecision decision, ModerationSanction sanction, ModerationReferenceRecognition purpose,
        DateTimeOffset capturedAt, DateTimeOffset retainUntil)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(sanction);
        decision.ValidatePersistedState();
        sanction.ValidatePersistedState();
        if (purpose is not (ModerationReferenceRecognition.SeparatelyQualifiedFinalAction
                or ModerationReferenceRecognition.SeparatelyQualifiedFinalEnrollment)
            || capturedAt.Offset != TimeSpan.Zero || capturedAt == DateTimeOffset.MinValue
            || retainUntil.Offset != TimeSpan.Zero || retainUntil <= capturedAt || retainUntil == DateTimeOffset.MaxValue
            || decision.State is not (ModerationDecisionState.Active or ModerationDecisionState.Scheduled)
            || decision.FindingId is null || decision.Subject.Kind != ModerationSubjectKind.Human
            || sanction.DecisionId != decision.Id || sanction.Subject != decision.Subject
            || sanction.Scope != decision.Scope || sanction.Rule != decision.Rule
            || sanction.CapabilityMask != decision.CapabilityMask || sanction.StartsAt != decision.StartsAt
            || sanction.EndsAt != decision.EndsAt || sanction.ReviewAt != decision.ReviewAt
            || capturedAt < sanction.UpdatedAt || !sanction.IsEffectiveAt(capturedAt))
            throw new ArgumentException("Recognition requires a final effective human contribution, exact original meaning and finite purpose retention.");
        var capabilities = sanction.Capabilities.Where(capability => purpose
            == ModerationReferenceRecognition.SeparatelyQualifiedFinalEnrollment
                ? capability is ModerationCapability.Enrollment or ModerationCapability.Admission or ModerationCapability.PrivateLogistics
                : capability is ModerationCapability.Publish or ModerationCapability.Upload
                    or ModerationCapability.ReadResource or ModerationCapability.Manage or ModerationCapability.Participate)
            .ToFrozenSet();
        if (capabilities.Count == 0)
            throw new ArgumentException("The original contribution contains no capability for this recognition purpose.");
        return new(decision, sanction, purpose, capabilities, capturedAt, retainUntil);
    }

    /// <summary>
    /// Rechecks only retained contribution meaning, not native authority or identity matching.
    /// The caller must separately prove the provider token, current policy and target scope.
    /// </summary>
    public bool MatchesCurrent(ModerationDecision decision, ModerationSanction sanction, DateTimeOffset now) =>
        now >= CapturedAt && now < RetainUntil && now.Offset == TimeSpan.Zero
        && decision.Id == DecisionId && decision.Revision >= DecisionRevision
        && decision.State is ModerationDecisionState.Active or ModerationDecisionState.Scheduled
        && decision.FindingId is not null && decision.Subject.Kind == ModerationSubjectKind.Human
        && sanction.Id == SanctionId && sanction.DecisionId == DecisionId && sanction.Revision >= SanctionRevision
        && sanction.Subject == decision.Subject && sanction.Scope == Scope && decision.Scope == Scope
        && sanction.CapabilityMask == OriginalCapabilityMask && decision.CapabilityMask == OriginalCapabilityMask
        && sanction.Rule == Rule && decision.Rule == Rule
        && sanction.StartsAt == StartsAt && decision.StartsAt == StartsAt
        && sanction.EndsAt == EndsAt && decision.EndsAt == EndsAt
        && sanction.ReviewAt == ReviewAt && decision.ReviewAt == ReviewAt
        && now >= sanction.UpdatedAt && now >= decision.UpdatedAt
        && sanction.IsEffectiveAt(now) && Capabilities.All(sanction.Capabilities.Contains);

    public override string ToString() => nameof(ModerationRecognitionContribution);
}
