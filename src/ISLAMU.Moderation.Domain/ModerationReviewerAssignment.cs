namespace ISLAMU.Moderation.Domain;

public enum ModerationReviewerConflictState
{
    Unresolved = 1,
    Independent = 2,
    Conflicted = 3
}

/// <summary>
/// Case-specific identity and conflict qualification. The host must also prove
/// current native reviewer permission; account inequality alone proves nothing.
/// </summary>
public sealed class ModerationReviewerAssignment
{
    private ModerationReviewerAssignment()
    {
    }

    public Guid Id { get; private set; }
    public Guid CaseId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid? CaseTenantId { get; private set; }
    public Guid ReviewerHumanId { get; private set; }
    public Guid? SubjectHumanId { get; private set; }
    public Guid AssignedByHumanId { get; private set; }
    public Guid? ControllerHumanId { get; private set; }
    public Guid? IdentityEvidenceId { get; private set; }
    public Guid? QualifiedByHumanId { get; private set; }
    public DateTimeOffset? QualifiedUntil { get; private set; }
    public ModerationReviewerConflictState ConflictState { get; private set; }
    public Guid? ConflictEvidenceId { get; private set; }
    public Guid? AssessedByHumanId { get; private set; }
    public Guid? WithdrawnByHumanId { get; private set; }
    public DateTimeOffset? WithdrawnAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public long Revision { get; private set; }

    public static ModerationReviewerAssignment Assign(
        Guid id, ModerationCase moderationCase, long expectedCaseRevision,
        Guid reviewerHumanId, Guid assignedByHumanId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(moderationCase);
        moderationCase.ValidatePersistedState();
        if (id == Guid.Empty || id.Version != 7 || id.Variant is < 8 or > 11
            || reviewerHumanId == Guid.Empty || assignedByHumanId == Guid.Empty
            || moderationCase.State != ModerationCaseState.Open
            || moderationCase.Revision != expectedCaseRevision || now < moderationCase.UpdatedAt
            || moderationCase.Subject.Kind == ModerationSubjectKind.Human
                && moderationCase.Subject.Id == reviewerHumanId)
        {
            throw new ArgumentException("Assignment requires a current open case and a distinct canonical human.");
        }

        return new ModerationReviewerAssignment
        {
            Id = id,
            CaseId = moderationCase.Id,
            TrustDomainId = moderationCase.Scope.TrustDomainId,
            CaseTenantId = moderationCase.ScopeTenantId,
            SubjectHumanId = moderationCase.Subject.Kind == ModerationSubjectKind.Human
                ? moderationCase.Subject.Id : null,
            ReviewerHumanId = reviewerHumanId,
            AssignedByHumanId = assignedByHumanId,
            ConflictState = ModerationReviewerConflictState.Unresolved,
            CreatedAt = now.ToUniversalTime(),
            UpdatedAt = now.ToUniversalTime(),
            Revision = 1
        };
    }

    /// <summary>Changing or renewing controller proof invalidates the prior conflict assessment.</summary>
    public void QualifyController(
        long expectedRevision, Guid controllerHumanId, Guid identityEvidenceId,
        Guid qualifiedByHumanId, DateTimeOffset qualifiedUntil, DateTimeOffset now)
    {
        RequireCurrent(expectedRevision, now);
        if (controllerHumanId == Guid.Empty || controllerHumanId == SubjectHumanId
            || identityEvidenceId == Guid.Empty || qualifiedByHumanId == Guid.Empty
            || qualifiedUntil <= now)
        {
            throw new ArgumentException("Current controller qualification requires independent source evidence and bounded validity.");
        }

        var nextRevision = checked(Revision + 1);
        ControllerHumanId = controllerHumanId;
        IdentityEvidenceId = identityEvidenceId;
        QualifiedByHumanId = qualifiedByHumanId;
        QualifiedUntil = qualifiedUntil.ToUniversalTime();
        ConflictState = ModerationReviewerConflictState.Unresolved;
        ConflictEvidenceId = null;
        AssessedByHumanId = null;
        UpdatedAt = now.ToUniversalTime();
        Revision = nextRevision;
    }

    public void RecordConflictAssessment(
        long expectedRevision, ModerationReviewerConflictState state,
        Guid conflictEvidenceId, Guid assessedByHumanId, DateTimeOffset now)
    {
        RequireCurrent(expectedRevision, now);
        if (ControllerHumanId is null || QualifiedUntil <= now
            || state is not (ModerationReviewerConflictState.Independent or ModerationReviewerConflictState.Conflicted)
            || conflictEvidenceId == Guid.Empty || assessedByHumanId == Guid.Empty
            || assessedByHumanId == ReviewerHumanId || assessedByHumanId == ControllerHumanId
            || assessedByHumanId == SubjectHumanId)
        {
            throw new ArgumentException("Conflict qualification requires a current controller and independent assessor.");
        }

        var nextRevision = checked(Revision + 1);
        ConflictState = state;
        ConflictEvidenceId = conflictEvidenceId;
        AssessedByHumanId = assessedByHumanId;
        UpdatedAt = now.ToUniversalTime();
        Revision = nextRevision;
    }

    public void Withdraw(long expectedRevision, Guid withdrawnByHumanId, DateTimeOffset now)
    {
        RequireCurrent(expectedRevision, now);
        if (withdrawnByHumanId == Guid.Empty)
            throw new ArgumentException("Withdrawal requires its canonical accountable human.", nameof(withdrawnByHumanId));

        var nextRevision = checked(Revision + 1);
        WithdrawnByHumanId = withdrawnByHumanId;
        WithdrawnAt = now.ToUniversalTime();
        UpdatedAt = WithdrawnAt.Value;
        Revision = nextRevision;
    }

    public bool IsQualifiedAt(DateTimeOffset now) =>
        WithdrawnAt is null && now >= UpdatedAt && now < QualifiedUntil
        && ControllerHumanId is not null && IdentityEvidenceId is not null
        && QualifiedByHumanId is not null && ConflictEvidenceId is not null
        && AssessedByHumanId is not null && ConflictState == ModerationReviewerConflictState.Independent;

    /// <summary>Uses positively recorded controller facts; unresolved equivalence never counts toward quorum.</summary>
    public bool IsIndependentOf(ModerationReviewerAssignment other, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(other);
        return CaseId == other.CaseId && TrustDomainId == other.TrustDomainId
            && IsQualifiedAt(now) && other.IsQualifiedAt(now)
            && ReviewerHumanId != other.ReviewerHumanId
            && ControllerHumanId != other.ControllerHumanId
            && ReviewerHumanId != other.ControllerHumanId
            && ControllerHumanId != other.ReviewerHumanId;
    }

    public void ValidatePersistedState()
    {
        var hasController = ControllerHumanId is not null;
        var hasAssessment = ConflictState != ModerationReviewerConflictState.Unresolved;
        if (Id == Guid.Empty || Id.Version != 7 || Id.Variant is < 8 or > 11
            || CaseId == Guid.Empty || TrustDomainId == Guid.Empty || CaseTenantId == Guid.Empty || ReviewerHumanId == Guid.Empty
            || AssignedByHumanId == Guid.Empty || SubjectHumanId == Guid.Empty
            || ReviewerHumanId == SubjectHumanId || Revision < 1 || UpdatedAt < CreatedAt
            || !Enum.IsDefined(ConflictState)
            || hasController != (IdentityEvidenceId is not null)
            || hasController != (QualifiedByHumanId is not null)
            || hasController != (QualifiedUntil is not null)
            || ControllerHumanId == Guid.Empty || ControllerHumanId is not null && ControllerHumanId == SubjectHumanId
            || IdentityEvidenceId == Guid.Empty || QualifiedByHumanId == Guid.Empty
            || QualifiedUntil is not null && QualifiedUntil <= CreatedAt
            || hasAssessment && !hasController
            || hasAssessment != (ConflictEvidenceId is not null)
            || hasAssessment != (AssessedByHumanId is not null)
            || ConflictEvidenceId == Guid.Empty || AssessedByHumanId == Guid.Empty
            || AssessedByHumanId is not null && (AssessedByHumanId == ReviewerHumanId
                || AssessedByHumanId == ControllerHumanId || AssessedByHumanId == SubjectHumanId)
            || (WithdrawnAt is not null) != (WithdrawnByHumanId is not null)
            || WithdrawnByHumanId == Guid.Empty || WithdrawnAt is not null && WithdrawnAt != UpdatedAt
            || Revision == 1 && (hasController || hasAssessment || WithdrawnAt is not null || UpdatedAt != CreatedAt))
        {
            throw new ArgumentException("Persisted reviewer ownership, qualification or revision is inconsistent.");
        }
    }

    private void RequireCurrent(long expectedRevision, DateTimeOffset now)
    {
        ValidatePersistedState();
        if (Revision != expectedRevision || WithdrawnAt is not null || now < UpdatedAt)
            throw new InvalidOperationException("The reviewer assignment is no longer current.");
    }

    public override string ToString() => nameof(ModerationReviewerAssignment);
}
