namespace ISLAMU.Moderation.Domain;

public enum ModerationScheduleRiskReviewState
{
    Pending = 1,
    Accepted = 2,
    Withdrawn = 3,
    Overdue = 4
}

/// <summary>
/// Separately owned source review work, never a contribution to personal restrictions.
/// Creation and acceptance validate lifecycle structure only. A native writer must first
/// admit current event-relevant risk and reviewer permission under its authority fences.
/// Accepted, withdrawn and overdue work still requires the adopted native fallback/settlement;
/// no state here authorizes disclosure, credentials, exclusion, cancellation or refunds.
/// </summary>
public sealed partial class ModerationScheduleRiskReview
{
    private ModerationScheduleRiskReview()
    {
    }

    private ModerationScheduleRiskReview(Guid id, ModerationSanction source,
        ModerationScheduleRiskCoordinates coordinates, SubjectReference owner, SubjectReference escalationOwner,
        DateTimeOffset dueAt, DateTimeOffset now)
    {
        Id = id;
        SourceSanctionId = source.Id;
        SourceSanctionRevision = source.Revision;
        SourceDecisionId = source.DecisionId;
        Subject = source.Subject;
        SourceScope = source.Scope;
        SourceRule = source.Rule;
        OriginalSanctionStartsAt = source.StartsAt;
        OriginalSanctionEndsAt = source.EndsAt;
        OriginalCoordinates = coordinates;
        Coordinates = coordinates;
        Owner = owner;
        EscalationOwner = escalationOwner;
        OriginalDueAt = dueAt;
        DueAt = dueAt;
        CreatedAt = now;
        UpdatedAt = now;
        State = ModerationScheduleRiskReviewState.Pending;
        Revision = 1;
    }

    public Guid Id { get; private set; }
    public Guid SourceSanctionId { get; private set; }
    public long SourceSanctionRevision { get; private set; }
    public Guid SourceDecisionId { get; private set; }
    public DateTimeOffset OriginalSanctionStartsAt { get; private set; }
    public DateTimeOffset? OriginalSanctionEndsAt { get; private set; }
    public DateTimeOffset OriginalDueAt { get; private set; }
    public DateTimeOffset DueAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public ModerationScheduleRiskReviewState State { get; private set; }
    public long Revision { get; private set; }
    public Guid? AcceptedIntentId { get; private set; }
    public Guid? AcceptedFindingId { get; private set; }
    public long? AcceptedFindingRevision { get; private set; }
    public Guid? AcceptedReviewerAssignmentId { get; private set; }
    public long? AcceptedReviewerAssignmentRevision { get; private set; }
    public Guid? WithdrawalIntentId { get; private set; }

    /// <summary>
    /// Records candidate owned work with immutable sanction ancestry. Source coordinates,
    /// an old sanction and a deadline alone cannot establish qualified risk or activate a fence.
    /// Stable identity deduplication and native admission belong to the owning writer.
    /// </summary>
    public static ModerationScheduleRiskReview Open(Guid id, ModerationSanction source,
        long expectedSourceRevision, ModerationScheduleRiskCoordinates coordinates,
        SubjectReference owner, SubjectReference escalationOwner, DateTimeOffset dueAt, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(coordinates);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(escalationOwner);
        source.ValidatePersistedState();
        RequireInstant(now);
        RequireDeadline(dueAt, now);
        if (id == Guid.Empty || id.Version != 7 || id.Variant is < 8 or > 11
            || expectedSourceRevision != source.Revision || now < source.UpdatedAt
            || source.Subject != coordinates.Participant || !source.Scope.Contains(coordinates.EventScope)
            || owner.Kind != ModerationSubjectKind.Human || escalationOwner.Kind != ModerationSubjectKind.Human
            || owner.TrustDomainId != source.Scope.TrustDomainId
            || escalationOwner.TrustDomainId != source.Scope.TrustDomainId)
            throw new ArgumentException("Separate review requires current source ancestry, exact participant/event and accountable owners.");

        var review = new ModerationScheduleRiskReview(id, source, coordinates, owner, escalationOwner, dueAt, now);
        review.ValidatePersistedState();
        return review;
    }

    /// <summary>Explicitly revises unreviewed work; overdue or terminal work cannot be restarted.</summary>
    public void Rebind(ModerationScheduleRiskCoordinates coordinates, DateTimeOffset dueAt,
        long expectedRevision, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        RequireCurrent(expectedRevision, now);
        RequirePendingBeforeDeadline(now);
        RequireDeadline(dueAt, now);
        if (!coordinates.IsSuccessorOf(Coordinates))
            throw new InvalidOperationException("Only forward revisions of the same event, participant and policy may rebind review work.");

        var nextRevision = checked(Revision + 1);
        Coordinates = coordinates;
        DueAt = dueAt;
        UpdatedAt = now;
        Revision = nextRevision;
    }

    /// <summary>
    /// Records a structurally matching review after native admission. Finding data and assignment
    /// shape are not qualified event-risk proof; this transition never supplies ordinary permission.
    /// A replay returns no new acceptance and cannot renew reviewer authority or any sanction.
    /// </summary>
    public bool Accept(Guid intentId, ModerationFinding finding, ModerationReviewerAssignment reviewer,
        long expectedReviewerRevision, ModerationScheduleRiskCoordinates currentCoordinates,
        long expectedRevision, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(finding);
        ArgumentNullException.ThrowIfNull(reviewer);
        ArgumentNullException.ThrowIfNull(currentCoordinates);
        RequireCurrent(expectedRevision, now);
        RequireIntent(intentId);
        if (State == ModerationScheduleRiskReviewState.Accepted && AcceptedIntentId == intentId
            && Coordinates == currentCoordinates && AcceptedFindingId == finding.Id
            && AcceptedFindingRevision == finding.Revision && AcceptedReviewerAssignmentId == reviewer.Id
            && AcceptedReviewerAssignmentRevision == expectedReviewerRevision
            && reviewer.Revision == expectedReviewerRevision)
            return false;

        RequirePendingBeforeDeadline(now);
        finding.ValidatePersistedState();
        reviewer.ValidatePersistedState();
        if (currentCoordinates != Coordinates || finding.State != ModerationFindingState.Final
            || finding.Subject != Subject || !finding.Scope.Contains(Coordinates.EventScope)
            || finding.Rule != Coordinates.Policy || finding.FinalizedAt < CreatedAt || finding.UpdatedAt > now
            || reviewer.CaseId != finding.CaseId || reviewer.TrustDomainId != SourceScope.TrustDomainId
            || reviewer.CaseTenantId != Coordinates.EventScope.Coordinates.TenantId
            || reviewer.SubjectHumanId != Subject.Id || reviewer.ReviewerHumanId != finding.Adjudicator.Id
            || expectedReviewerRevision != reviewer.Revision || !reviewer.IsQualifiedAt(now))
            throw new InvalidOperationException("Review acceptance requires exact current coordinates and an independent unexpired case reviewer.");

        var nextRevision = checked(Revision + 1);
        AcceptedIntentId = intentId;
        AcceptedFindingId = finding.Id;
        AcceptedFindingRevision = finding.Revision;
        AcceptedReviewerAssignmentId = reviewer.Id;
        AcceptedReviewerAssignmentRevision = reviewer.Revision;
        State = ModerationScheduleRiskReviewState.Accepted;
        UpdatedAt = now;
        Revision = nextRevision;
        return true;
    }

    /// <summary>Records withdrawal only; unsupported risk does not automatically release or settle anything.</summary>
    public bool Withdraw(Guid intentId, SubjectReference withdrawnBy, long expectedRevision, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(withdrawnBy);
        RequireCurrent(expectedRevision, now);
        RequireIntent(intentId);
        if (State == ModerationScheduleRiskReviewState.Withdrawn
            && WithdrawalIntentId == intentId && WithdrawnBy == withdrawnBy)
            return false;
        RequirePendingBeforeDeadline(now);
        if (withdrawnBy != Owner)
            throw new InvalidOperationException("Withdrawal requires the current accountable review owner.");

        var nextRevision = checked(Revision + 1);
        WithdrawalIntentId = intentId;
        WithdrawnBy = withdrawnBy;
        State = ModerationScheduleRiskReviewState.Withdrawn;
        UpdatedAt = now;
        Revision = nextRevision;
        return true;
    }

    public bool IsOverdueAt(DateTimeOffset now)
    {
        ValidatePersistedState();
        RequireInstant(now);
        if (now < UpdatedAt)
            throw new InvalidOperationException("The review observation cannot regress.");
        return State == ModerationScheduleRiskReviewState.Overdue
            || State == ModerationScheduleRiskReviewState.Pending && now >= DueAt;
    }

    /// <summary>Materializes overdue work for its escalation owner, never extending the original sanction.</summary>
    public bool Reconcile(long expectedRevision, DateTimeOffset now)
    {
        RequireCurrent(expectedRevision, now);
        if (State != ModerationScheduleRiskReviewState.Pending || !IsOverdueAt(now))
            return false;
        var nextRevision = checked(Revision + 1);
        State = ModerationScheduleRiskReviewState.Overdue;
        UpdatedAt = now;
        Revision = nextRevision;
        return true;
    }

    private void RequireCurrent(long expectedRevision, DateTimeOffset now)
    {
        ValidatePersistedState();
        RequireInstant(now);
        if (expectedRevision != Revision || now < UpdatedAt)
            throw new InvalidOperationException("The review revision or transition instant is not current.");
    }

    private void RequirePendingBeforeDeadline(DateTimeOffset now)
    {
        if (State != ModerationScheduleRiskReviewState.Pending || now >= DueAt)
            throw new InvalidOperationException("Only pending review before its explicit deadline can transition.");
    }

    private static void RequireIntent(Guid intentId)
    {
        if (intentId == Guid.Empty || intentId.Version != 7 || intentId.Variant is < 8 or > 11)
            throw new ArgumentException("A stable review intent is required.", nameof(intentId));
    }

    private static void RequireDeadline(DateTimeOffset dueAt, DateTimeOffset now)
    {
        RequireInstant(dueAt);
        if (dueAt <= now)
            throw new ArgumentException("A separate review requires an explicit future finite deadline.", nameof(dueAt));
    }

    private static void RequireInstant(DateTimeOffset instant)
    {
        if (instant.Offset != TimeSpan.Zero || instant == DateTimeOffset.MinValue || instant == DateTimeOffset.MaxValue)
            throw new ArgumentException("Review instants must be canonical UTC and finite.");
    }

    /// <summary>
    /// Rejects incomplete retained ancestry and mutually inconsistent terminal receipts.
    /// Validation checks stored structure; it never establishes current native risk or authority.
    /// </summary>
    public void ValidatePersistedState()
    {
        RequireIntent(Id);
        RequireIntent(SourceSanctionId);
        RequireIntent(SourceDecisionId);
        RequireInstant(OriginalSanctionStartsAt);
        if (OriginalSanctionEndsAt is { } originalEnd)
        {
            RequireInstant(originalEnd);
            if (originalEnd <= OriginalSanctionStartsAt)
                throw new ArgumentException("Retained source interval is inconsistent.");
        }
        RequireInstant(CreatedAt);
        RequireInstant(UpdatedAt);
        RequireDeadline(OriginalDueAt, CreatedAt);
        RequireDeadline(DueAt, CreatedAt);
        if (Subject is null || SourceScope is null || SourceRule is null
            || OriginalCoordinates is null || Coordinates is null || Owner is null || EscalationOwner is null
            || SourceSanctionRevision < 1 || Revision < 1 || UpdatedAt < CreatedAt || !Enum.IsDefined(State)
            || Subject != OriginalCoordinates.Participant || Subject != Coordinates.Participant
            || !SourceScope.Contains(OriginalCoordinates.EventScope)
            || !SourceScope.Contains(Coordinates.EventScope)
            || Coordinates != OriginalCoordinates && !Coordinates.IsSuccessorOf(OriginalCoordinates)
            || Owner.Kind != ModerationSubjectKind.Human || EscalationOwner.Kind != ModerationSubjectKind.Human
            || Owner.TrustDomainId != SourceScope.TrustDomainId || EscalationOwner.TrustDomainId != SourceScope.TrustDomainId
            || Revision == 1 && (State != ModerationScheduleRiskReviewState.Pending
                || UpdatedAt != CreatedAt || Coordinates != OriginalCoordinates || DueAt != OriginalDueAt)
            || State == ModerationScheduleRiskReviewState.Overdue && UpdatedAt < DueAt
            || State != ModerationScheduleRiskReviewState.Overdue && UpdatedAt >= DueAt)
            throw new ArgumentException("Retained review ancestry, revision or deadline is inconsistent.");

        var accepted = State == ModerationScheduleRiskReviewState.Accepted;
        var withdrawn = State == ModerationScheduleRiskReviewState.Withdrawn;
        if (accepted != AcceptedIntentId.HasValue || accepted != AcceptedFindingId.HasValue
            || accepted != AcceptedFindingRevision.HasValue || accepted != AcceptedReviewerAssignmentId.HasValue
            || accepted != AcceptedReviewerAssignmentRevision.HasValue
            || withdrawn != WithdrawalIntentId.HasValue || withdrawn != (WithdrawnBy is not null)
            || AcceptedFindingRevision is < 1 || AcceptedReviewerAssignmentRevision is < 1
            || withdrawn && WithdrawnBy != Owner)
            throw new ArgumentException("Retained review terminal provenance is inconsistent.");
        if (accepted)
        {
            RequireIntent(AcceptedIntentId!.Value);
            RequireIntent(AcceptedFindingId!.Value);
            RequireIntent(AcceptedReviewerAssignmentId!.Value);
        }
        if (withdrawn) RequireIntent(WithdrawalIntentId!.Value);
    }

    public override string ToString() => nameof(ModerationScheduleRiskReview);
}
