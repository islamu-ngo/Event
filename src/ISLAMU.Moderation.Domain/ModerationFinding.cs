namespace ISLAMU.Moderation.Domain;

public enum ModerationFindingState
{
    Final = 1,
    Reversed = 2
}

public enum ModerationFindingEvidencePurpose
{
    Response = 1,
    QualifiedUrgency = 2
}

/// <summary>
/// A purpose-specific evidence reference bound to one case revision. The writer must establish
/// the referenced response or urgency qualification under its authority fences; this is not that proof.
/// </summary>
public sealed record ModerationFindingEvidenceReference
{
    private ModerationFindingEvidenceReference(
        ModerationFindingEvidencePurpose purpose, Guid referenceId, Guid caseId, long caseRevision,
        Guid? urgencyQualificationId, DateTimeOffset recordedAt)
    {
        Purpose = purpose;
        ReferenceId = referenceId;
        CaseId = caseId;
        CaseRevision = caseRevision;
        UrgencyQualificationId = urgencyQualificationId;
        RecordedAt = recordedAt.ToUniversalTime();
    }

    public ModerationFindingEvidencePurpose Purpose { get; }
    public Guid ReferenceId { get; }
    public Guid CaseId { get; }
    public long CaseRevision { get; }
    public Guid? UrgencyQualificationId { get; }
    public DateTimeOffset RecordedAt { get; }

    public static ModerationFindingEvidenceReference Response(
        Guid referenceId, Guid caseId, long caseRevision, DateTimeOffset recordedAt) =>
        FromCoordinates(ModerationFindingEvidencePurpose.Response, referenceId, caseId, caseRevision, null, recordedAt);

    public static ModerationFindingEvidenceReference QualifiedUrgency(
        Guid referenceId, Guid qualificationId, Guid caseId, long caseRevision, DateTimeOffset recordedAt) =>
        FromCoordinates(ModerationFindingEvidencePurpose.QualifiedUrgency,
            referenceId, caseId, caseRevision, qualificationId, recordedAt);

    internal static ModerationFindingEvidenceReference FromCoordinates(
        ModerationFindingEvidencePurpose purpose, Guid referenceId, Guid caseId, long caseRevision,
        Guid? urgencyQualificationId, DateTimeOffset recordedAt)
    {
        var purposeIsValid = purpose switch
        {
            ModerationFindingEvidencePurpose.Response => urgencyQualificationId is null,
            ModerationFindingEvidencePurpose.QualifiedUrgency => urgencyQualificationId is { } qualification
                && qualification != Guid.Empty && qualification != referenceId,
            _ => false
        };
        if (!purposeIsValid || referenceId == Guid.Empty || caseId == Guid.Empty
            || caseRevision < 1 || recordedAt == default)
        {
            throw new ArgumentException("Evidence requires a qualified case revision and purpose-distinct reference.");
        }

        return new ModerationFindingEvidenceReference(
            purpose, referenceId, caseId, caseRevision, urgencyQualificationId, recordedAt);
    }

    public override string ToString() => Purpose.ToString();
}

/// <summary>
/// Permanent checked finding authority. Reversal appends an irreversible audit without changing
/// final ownership. Permission, controller conflicts and current rule adoption remain fenced writer obligations.
/// </summary>
public sealed class ModerationFinding
{
    private ModerationFinding()
    {
    }

    public Guid Id { get; private set; }
    public Guid CaseId { get; private set; }
    public Guid CaseOccurrenceId { get; private set; }
    public long CaseRevision { get; private set; }
    public DateTimeOffset CaseCreatedAt { get; private set; }
    public ModerationSubjectKind SubjectKind { get; private set; }
    public Guid SubjectId { get; private set; }
    public ModerationScopeKind SubjectOriginKind { get; private set; }
    public Guid SubjectOriginTrustDomainId { get; private set; }
    public Guid? SubjectOriginSolutionId { get; private set; }
    public Guid? SubjectOriginInstanceId { get; private set; }
    public Guid? SubjectOriginTenantId { get; private set; }
    public Guid? SubjectOriginCommunityId { get; private set; }
    public Guid? SubjectOriginResourceId { get; private set; }
    public Guid? SubjectOriginEventId { get; private set; }
    public ModerationScopeKind ScopeKind { get; private set; }
    public Guid ScopeTrustDomainId { get; private set; }
    public Guid? ScopeSolutionId { get; private set; }
    public Guid? ScopeInstanceId { get; private set; }
    public Guid? ScopeTenantId { get; private set; }
    public Guid? ScopeCommunityId { get; private set; }
    public Guid? ScopeResourceId { get; private set; }
    public Guid? ScopeEventId { get; private set; }
    public Guid RuleOwnerId { get; private set; }
    public string RuleCode { get; private set; } = string.Empty;
    public long RuleRevision { get; private set; }
    public ModerationSubjectKind AdjudicatorKind { get; private set; }
    public Guid AdjudicatorId { get; private set; }
    public ModerationScopeKind AdjudicatorOriginKind { get; private set; }
    public Guid AdjudicatorOriginTrustDomainId { get; private set; }
    public Guid? AdjudicatorOriginSolutionId { get; private set; }
    public Guid? AdjudicatorOriginInstanceId { get; private set; }
    public Guid? AdjudicatorOriginTenantId { get; private set; }
    public Guid? AdjudicatorOriginCommunityId { get; private set; }
    public Guid? AdjudicatorOriginResourceId { get; private set; }
    public Guid? AdjudicatorOriginEventId { get; private set; }
    public ModerationFindingEvidencePurpose EvidencePurpose { get; private set; }
    public Guid EvidenceReferenceId { get; private set; }
    public Guid? UrgencyQualificationId { get; private set; }
    public DateTimeOffset EvidenceRecordedAt { get; private set; }
    public string Justification { get; private set; } = string.Empty;
    public DateTimeOffset FinalizedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public ModerationFindingState State { get; private set; }
    public long Revision { get; private set; }
    public Guid? ReversalReferenceId { get; private set; }
    public ModerationSubjectKind? ReversedByKind { get; private set; }
    public Guid? ReversedById { get; private set; }
    public ModerationScopeKind? ReversedByOriginKind { get; private set; }
    public Guid? ReversedByOriginTrustDomainId { get; private set; }
    public Guid? ReversedByOriginSolutionId { get; private set; }
    public Guid? ReversedByOriginInstanceId { get; private set; }
    public Guid? ReversedByOriginTenantId { get; private set; }
    public Guid? ReversedByOriginCommunityId { get; private set; }
    public Guid? ReversedByOriginResourceId { get; private set; }
    public Guid? ReversedByOriginEventId { get; private set; }
    public string? ReversalJustification { get; private set; }
    public DateTimeOffset? ReversedAt { get; private set; }

    public SubjectReference Subject
    {
        get => SubjectReference.Create(SubjectKind, SubjectId, AuthorityScope.FromCoordinates(
            SubjectOriginKind, SubjectOriginTrustDomainId, SubjectOriginSolutionId, SubjectOriginInstanceId,
            SubjectOriginTenantId, SubjectOriginCommunityId, SubjectOriginResourceId, SubjectOriginEventId));
        private set
        {
            SubjectKind = value.Kind;
            SubjectId = value.Id;
            (SubjectOriginKind, SubjectOriginTrustDomainId, SubjectOriginSolutionId, SubjectOriginInstanceId,
                SubjectOriginTenantId, SubjectOriginCommunityId, SubjectOriginResourceId, SubjectOriginEventId) = value.Origin.Coordinates;
        }
    }

    public AuthorityScope Scope
    {
        get => AuthorityScope.FromCoordinates(ScopeKind, ScopeTrustDomainId, ScopeSolutionId, ScopeInstanceId,
            ScopeTenantId, ScopeCommunityId, ScopeResourceId, ScopeEventId);
        private set => (ScopeKind, ScopeTrustDomainId, ScopeSolutionId, ScopeInstanceId,
            ScopeTenantId, ScopeCommunityId, ScopeResourceId, ScopeEventId) = value.Coordinates;
    }

    public ModerationRuleReference Rule
    {
        get => ModerationRuleReference.Create(RuleOwnerId, RuleCode, RuleRevision);
        private set => (RuleOwnerId, RuleCode, RuleRevision) = (value.OwnerId, value.Code, value.Revision);
    }

    public SubjectReference Adjudicator
    {
        get => SubjectReference.Create(AdjudicatorKind, AdjudicatorId, AuthorityScope.FromCoordinates(
            AdjudicatorOriginKind, AdjudicatorOriginTrustDomainId, AdjudicatorOriginSolutionId,
            AdjudicatorOriginInstanceId, AdjudicatorOriginTenantId, AdjudicatorOriginCommunityId,
            AdjudicatorOriginResourceId, AdjudicatorOriginEventId));
        private set
        {
            AdjudicatorKind = value.Kind;
            AdjudicatorId = value.Id;
            (AdjudicatorOriginKind, AdjudicatorOriginTrustDomainId, AdjudicatorOriginSolutionId,
                AdjudicatorOriginInstanceId, AdjudicatorOriginTenantId, AdjudicatorOriginCommunityId,
                AdjudicatorOriginResourceId, AdjudicatorOriginEventId) = value.Origin.Coordinates;
        }
    }

    public ModerationFindingEvidenceReference Evidence => ModerationFindingEvidenceReference.FromCoordinates(
        EvidencePurpose, EvidenceReferenceId, CaseId, CaseRevision, UrgencyQualificationId, EvidenceRecordedAt);

    public SubjectReference? ReversedBy => SubjectReference.FromOptionalCoordinates(
        ReversedByKind, ReversedById, ReversedByOriginKind, ReversedByOriginTrustDomainId,
        ReversedByOriginSolutionId, ReversedByOriginInstanceId, ReversedByOriginTenantId,
        ReversedByOriginCommunityId, ReversedByOriginResourceId, ReversedByOriginEventId);

    /// <summary>
    /// The materialization boundary must invoke this after scalar hydration and before publishing the entity.
    /// It checks semantic structure and audit sequencing, not external evidence or historical authorization.
    /// </summary>
    public void ValidatePersistedState()
    {
        var subject = Subject;
        var scope = Scope;
        RequireIndependentHuman(Adjudicator, subject, scope);
        _ = Rule;
        var evidence = Evidence;
        var reversedBy = ReversedBy;
        RequireJustification(Justification);
        if (Id == Guid.Empty || Id.Version != 7 || Id.Variant is < 8 or > 11
            || CaseId == Guid.Empty || CaseOccurrenceId == Guid.Empty || CaseRevision != 1
            || subject.TrustDomainId != scope.TrustDomainId
            || CaseCreatedAt == default || FinalizedAt == default
            || evidence.RecordedAt < CaseCreatedAt || evidence.RecordedAt > FinalizedAt
            || UpdatedAt < FinalizedAt)
        {
            throw new ArgumentException("The persisted finding ownership, evidence or finalization audit is inconsistent.");
        }

        switch (State)
        {
            case ModerationFindingState.Final when Revision == 1 && UpdatedAt == FinalizedAt
                && ReversalReferenceId is null && reversedBy is null
                && ReversalJustification is null && ReversedAt is null:
                return;
            case ModerationFindingState.Reversed when Revision == 2
                && ReversalReferenceId is { } reference && reference != Guid.Empty
                && reversedBy is not null && ReversedAt is { } reversedAt
                && reversedAt >= FinalizedAt && UpdatedAt == reversedAt:
                RequireIndependentHuman(reversedBy, subject, scope);
                RequireJustification(ReversalJustification);
                return;
            default:
                throw new ArgumentException("The persisted finding state, revision and reversal audit are inconsistent.");
        }
    }

    /// <summary>
    /// Finalizes against an actual current open case and the exact adopted rule identity supplied by the fenced writer.
    /// Neither a reference nor matching rule identity proves actual permission, conflict clearance or adoption.
    /// </summary>
    public static ModerationFinding Finalize(
        Guid id, ModerationCase moderationCase, long expectedCaseRevision, ModerationRuleReference rule,
        ModerationRuleReference adoptedRule, SubjectReference adjudicator,
        ModerationFindingEvidenceReference evidence, string justification, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(moderationCase);
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(adoptedRule);
        ArgumentNullException.ThrowIfNull(adjudicator);
        ArgumentNullException.ThrowIfNull(evidence);
        moderationCase.ValidatePersistedState();
        if (moderationCase.State != ModerationCaseState.Open || expectedCaseRevision != moderationCase.Revision
            || now < moderationCase.UpdatedAt)
        {
            throw new InvalidOperationException("Finalization requires the current open case revision.");
        }

        if (id == Guid.Empty || id.Version != 7 || id.Variant is < 8 or > 11 || rule != adoptedRule
            || evidence.CaseId != moderationCase.Id || evidence.CaseRevision != expectedCaseRevision
            || evidence.RecordedAt < moderationCase.CreatedAt || evidence.RecordedAt > now)
        {
            throw new ArgumentException("Finding identity, exact adopted rule and case-bound evidence are required.");
        }

        RequireIndependentHuman(adjudicator, moderationCase.Subject, moderationCase.Scope);
        RequireJustification(justification);
        var finding = new ModerationFinding
        {
            Id = id,
            CaseId = moderationCase.Id,
            CaseOccurrenceId = moderationCase.OccurrenceId,
            CaseRevision = moderationCase.Revision,
            CaseCreatedAt = moderationCase.CreatedAt,
            Subject = moderationCase.Subject,
            Scope = moderationCase.Scope,
            Rule = rule,
            Adjudicator = adjudicator,
            EvidencePurpose = evidence.Purpose,
            EvidenceReferenceId = evidence.ReferenceId,
            UrgencyQualificationId = evidence.UrgencyQualificationId,
            EvidenceRecordedAt = evidence.RecordedAt,
            Justification = justification,
            FinalizedAt = now.ToUniversalTime(),
            UpdatedAt = now.ToUniversalTime(),
            State = ModerationFindingState.Final,
            Revision = 1
        };
        finding.ValidatePersistedState();
        return finding;
    }

    /// <summary>Appends one permanent reversal after the writer qualifies current reversal authority.</summary>
    public void Reverse(
        Guid reversalReferenceId, SubjectReference reversedBy, string justification,
        long expectedRevision, DateTimeOffset now)
    {
        ValidatePersistedState();
        ArgumentNullException.ThrowIfNull(reversedBy);
        if (State != ModerationFindingState.Final || expectedRevision != Revision || now < UpdatedAt)
        {
            throw new InvalidOperationException("Only the exact current final finding revision can be reversed.");
        }

        if (reversalReferenceId == Guid.Empty)
        {
            throw new ArgumentException("An auditable reversal reference is required.", nameof(reversalReferenceId));
        }

        RequireIndependentHuman(reversedBy, Subject, Scope);
        RequireJustification(justification);
        var nextRevision = checked(Revision + 1);
        ReversalReferenceId = reversalReferenceId;
        ReversedByKind = reversedBy.Kind;
        ReversedById = reversedBy.Id;
        (ReversedByOriginKind, ReversedByOriginTrustDomainId, ReversedByOriginSolutionId,
            ReversedByOriginInstanceId, ReversedByOriginTenantId, ReversedByOriginCommunityId,
            ReversedByOriginResourceId, ReversedByOriginEventId) = reversedBy.Origin.Coordinates;
        ReversalJustification = justification;
        ReversedAt = now.ToUniversalTime();
        UpdatedAt = ReversedAt.Value;
        State = ModerationFindingState.Reversed;
        Revision = nextRevision;
    }

    private static void RequireIndependentHuman(
        SubjectReference actor, SubjectReference subject, AuthorityScope scope)
    {
        if (actor.Kind != ModerationSubjectKind.Human || actor.Id == subject.Id
            || actor.TrustDomainId != scope.TrustDomainId)
        {
            throw new ArgumentException("The finding actor must be a distinct canonical human in its trust domain.");
        }
    }

    private static void RequireJustification(string? justification)
    {
        if (string.IsNullOrWhiteSpace(justification) || justification.Length > 4000)
        {
            throw new ArgumentException("A substantive justification of at most 4000 characters is required.");
        }
    }
}
