namespace ISLAMU.Moderation.Domain;

public enum ModerationDecisionState
{
    Proposed = 1,
    AwaitingFinding = 2,
    AwaitingApproval = 3,
    Approved = 4,
    Scheduled = 5,
    Active = 6,
    Rejected = 7,
    Withdrawn = 8
}

/// <summary>Owns qualified finding, response and independent approval sequencing.</summary>
public sealed class ModerationDecision
{
    private readonly List<SubjectReference> _approvals = [];

    private ModerationDecision(
        Guid id, Guid caseId, SubjectReference subject, SubjectReference issuer,
        AuthorityScope issuingScope, AuthorityScope scope, ModerationRuleReference rule,
        IEnumerable<ModerationCapability> capabilities, DateTimeOffset startsAt, DateTimeOffset? endsAt,
        DateTimeOffset? reviewAt, int requiredApprovals, DateTimeOffset now)
    {
        Id = id;
        CaseId = caseId;
        Subject = subject;
        Issuer = issuer;
        IssuingScope = issuingScope;
        Scope = scope;
        Rule = rule;
        Capabilities = EffectiveRestrictions.CreateCapabilitySet(capabilities);
        StartsAt = startsAt.ToUniversalTime();
        EndsAt = endsAt?.ToUniversalTime();
        ReviewAt = reviewAt?.ToUniversalTime();
        RequiredApprovals = requiredApprovals;
        CreatedAt = now.ToUniversalTime();
        UpdatedAt = CreatedAt;
        State = ModerationDecisionState.Proposed;
        Approvals = _approvals.AsReadOnly();
    }

    public Guid Id { get; }
    public Guid CaseId { get; }
    public SubjectReference Subject { get; }
    public SubjectReference Issuer { get; }
    public AuthorityScope IssuingScope { get; }
    public AuthorityScope Scope { get; }
    public ModerationRuleReference Rule { get; }
    public IReadOnlySet<ModerationCapability> Capabilities { get; }
    public DateTimeOffset StartsAt { get; }
    public DateTimeOffset? EndsAt { get; }
    public DateTimeOffset? ReviewAt { get; }
    public int RequiredApprovals { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? FindingId { get; private set; }
    public IReadOnlyList<SubjectReference> Approvals { get; }
    public ModerationDecisionState State { get; private set; }
    public long Revision { get; private set; } = 1;

    public static ModerationDecision Propose(
        Guid id, Guid caseId, SubjectReference subject, SubjectReference issuer,
        AuthorityScope issuingScope, AuthorityScope scope, ModerationRuleReference rule,
        IEnumerable<ModerationCapability> capabilities, DateTimeOffset startsAt, DateTimeOffset? endsAt,
        DateTimeOffset? reviewAt, int requiredApprovals, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(issuingScope);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(rule);
        if (id == Guid.Empty || caseId == Guid.Empty)
        {
            throw new ArgumentException("Decision and case identifiers are required.", nameof(id));
        }

        if (!issuingScope.Contains(scope)
            || subject.TrustDomainId != scope.TrustDomainId
            || issuer.TrustDomainId != scope.TrustDomainId)
        {
            throw new ArgumentException("A decision cannot exceed its qualified jurisdiction.", nameof(scope));
        }

        if (requiredApprovals is < 1 or > 2
            || (scope.Kind is ModerationScopeKind.TrustDomain or ModerationScopeKind.Solution && requiredApprovals != 2))
        {
            throw new ArgumentException("The decision requires a valid independent controller quorum.", nameof(requiredApprovals));
        }

        if ((endsAt is not null && endsAt <= startsAt)
            || (endsAt is null && (reviewAt is null || requiredApprovals != 2))
            || (reviewAt is not null && (reviewAt <= startsAt || (endsAt is not null && reviewAt > endsAt))))
        {
            throw new ArgumentException("The decision interval and review deadline are inconsistent.", nameof(endsAt));
        }

        return new ModerationDecision(id, caseId, subject, issuer, issuingScope, scope, rule, capabilities,
            startsAt, endsAt, reviewAt, requiredApprovals, now);
    }

    public void RecordResponse(long expectedRevision, DateTimeOffset now)
    {
        RequireState(expectedRevision, now, ModerationDecisionState.Proposed);
        var nextRevision = checked(Revision + 1);
        State = ModerationDecisionState.AwaitingFinding;
        UpdatedAt = now.ToUniversalTime();
        Revision = nextRevision;
    }

    /// <summary>Accepts the finding identity after the host has positively qualified its current authority.</summary>
    public void AcceptFinding(Guid findingId, long expectedRevision, DateTimeOffset now)
    {
        RequireState(expectedRevision, now, ModerationDecisionState.AwaitingFinding);
        if (findingId == Guid.Empty)
        {
            throw new ArgumentException("A qualified finding identifier is required.", nameof(findingId));
        }

        var nextRevision = checked(Revision + 1);
        FindingId = findingId;
        State = ModerationDecisionState.AwaitingApproval;
        UpdatedAt = now.ToUniversalTime();
        Revision = nextRevision;
    }

    /// <summary>Counts canonical human controllers, never delegated keys or provider aliases as independent people.</summary>
    public void Approve(SubjectReference controller, AuthorityScope qualification, long expectedRevision, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(qualification);
        RequireState(expectedRevision, now, ModerationDecisionState.AwaitingApproval);
        if (controller.Kind != ModerationSubjectKind.Human
            || controller.TrustDomainId != Scope.TrustDomainId
            || !qualification.Contains(Scope)
            || controller.Id == Issuer.Id
            || controller.Id == Subject.Id
            || _approvals.Any(approval => approval.Id == controller.Id))
        {
            throw new InvalidOperationException("The controller is not qualified and independent for this approval.");
        }

        var nextRevision = checked(Revision + 1);
        _approvals.Add(controller);
        State = _approvals.Count == RequiredApprovals
            ? ModerationDecisionState.Approved
            : ModerationDecisionState.AwaitingApproval;
        UpdatedAt = now.ToUniversalTime();
        Revision = nextRevision;
    }

    public ModerationSanction Activate(Guid sanctionId, long expectedRevision, DateTimeOffset now)
    {
        RequireState(expectedRevision, now, ModerationDecisionState.Approved);
        if (sanctionId == Guid.Empty)
        {
            throw new ArgumentException("A stable contribution identifier is required.", nameof(sanctionId));
        }

        if (EndsAt is not null && now >= EndsAt)
        {
            throw new InvalidOperationException("An already ended decision cannot create an effective contribution.");
        }

        var nextRevision = checked(Revision + 1);
        var sanction = new ModerationSanction(sanctionId, this, now);
        State = now < StartsAt ? ModerationDecisionState.Scheduled : ModerationDecisionState.Active;
        UpdatedAt = now.ToUniversalTime();
        Revision = nextRevision;
        return sanction;
    }

    public void Reject(long expectedRevision, DateTimeOffset now) =>
        CloseProposal(ModerationDecisionState.Rejected, expectedRevision, now);

    public void Withdraw(long expectedRevision, DateTimeOffset now) =>
        CloseProposal(ModerationDecisionState.Withdrawn, expectedRevision, now);

    private void CloseProposal(ModerationDecisionState terminal, long expectedRevision, DateTimeOffset now)
    {
        if (expectedRevision != Revision || now < UpdatedAt
            || State is not (ModerationDecisionState.Proposed or ModerationDecisionState.AwaitingFinding
                or ModerationDecisionState.AwaitingApproval or ModerationDecisionState.Approved))
        {
            throw new InvalidOperationException("Only a current unactivated proposal can close.");
        }

        var nextRevision = checked(Revision + 1);
        State = terminal;
        UpdatedAt = now.ToUniversalTime();
        Revision = nextRevision;
    }

    private void RequireState(long expectedRevision, DateTimeOffset now, ModerationDecisionState expectedState)
    {
        if (expectedRevision != Revision || State != expectedState || now < UpdatedAt)
        {
            throw new InvalidOperationException("The decision revision, state or transition instant is not current.");
        }
    }
}
