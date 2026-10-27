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
    private ModerationDecision()
    {
    }

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
        CapabilityMask = EffectiveRestrictions.CreateCapabilityMask(capabilities);
        StartsAt = startsAt.ToUniversalTime();
        EndsAt = endsAt?.ToUniversalTime();
        ReviewAt = reviewAt?.ToUniversalTime();
        RequiredApprovals = requiredApprovals;
        CreatedAt = now.ToUniversalTime();
        UpdatedAt = CreatedAt;
        State = ModerationDecisionState.Proposed;
        Revision = 1;
    }

    public Guid Id { get; private set; }
    public Guid CaseId { get; private set; }
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
    public ModerationSubjectKind IssuerKind { get; private set; }
    public Guid IssuerId { get; private set; }
    public ModerationScopeKind IssuerOriginKind { get; private set; }
    public Guid IssuerOriginTrustDomainId { get; private set; }
    public Guid? IssuerOriginSolutionId { get; private set; }
    public Guid? IssuerOriginInstanceId { get; private set; }
    public Guid? IssuerOriginTenantId { get; private set; }
    public Guid? IssuerOriginCommunityId { get; private set; }
    public Guid? IssuerOriginResourceId { get; private set; }
    public Guid? IssuerOriginEventId { get; private set; }
    public ModerationScopeKind IssuingScopeKind { get; private set; }
    public Guid IssuingScopeTrustDomainId { get; private set; }
    public Guid? IssuingScopeSolutionId { get; private set; }
    public Guid? IssuingScopeInstanceId { get; private set; }
    public Guid? IssuingScopeTenantId { get; private set; }
    public Guid? IssuingScopeCommunityId { get; private set; }
    public Guid? IssuingScopeResourceId { get; private set; }
    public Guid? IssuingScopeEventId { get; private set; }
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
    public int CapabilityMask { get; private set; }
    public ModerationSubjectKind? Approval1Kind { get; private set; }
    public Guid? Approval1Id { get; private set; }
    public ModerationScopeKind? Approval1OriginKind { get; private set; }
    public Guid? Approval1OriginTrustDomainId { get; private set; }
    public Guid? Approval1OriginSolutionId { get; private set; }
    public Guid? Approval1OriginInstanceId { get; private set; }
    public Guid? Approval1OriginTenantId { get; private set; }
    public Guid? Approval1OriginCommunityId { get; private set; }
    public Guid? Approval1OriginResourceId { get; private set; }
    public Guid? Approval1OriginEventId { get; private set; }
    public ModerationSubjectKind? Approval2Kind { get; private set; }
    public Guid? Approval2Id { get; private set; }
    public ModerationScopeKind? Approval2OriginKind { get; private set; }
    public Guid? Approval2OriginTrustDomainId { get; private set; }
    public Guid? Approval2OriginSolutionId { get; private set; }
    public Guid? Approval2OriginInstanceId { get; private set; }
    public Guid? Approval2OriginTenantId { get; private set; }
    public Guid? Approval2OriginCommunityId { get; private set; }
    public Guid? Approval2OriginResourceId { get; private set; }
    public Guid? Approval2OriginEventId { get; private set; }

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

    public SubjectReference Issuer
    {
        get => SubjectReference.Create(IssuerKind, IssuerId, AuthorityScope.FromCoordinates(
            IssuerOriginKind, IssuerOriginTrustDomainId, IssuerOriginSolutionId, IssuerOriginInstanceId,
            IssuerOriginTenantId, IssuerOriginCommunityId, IssuerOriginResourceId, IssuerOriginEventId));
        private set
        {
            IssuerKind = value.Kind;
            IssuerId = value.Id;
            (IssuerOriginKind, IssuerOriginTrustDomainId, IssuerOriginSolutionId, IssuerOriginInstanceId,
                IssuerOriginTenantId, IssuerOriginCommunityId, IssuerOriginResourceId, IssuerOriginEventId) = value.Origin.Coordinates;
        }
    }

    public AuthorityScope IssuingScope
    {
        get => AuthorityScope.FromCoordinates(IssuingScopeKind, IssuingScopeTrustDomainId, IssuingScopeSolutionId,
            IssuingScopeInstanceId, IssuingScopeTenantId, IssuingScopeCommunityId, IssuingScopeResourceId, IssuingScopeEventId);
        private set => (IssuingScopeKind, IssuingScopeTrustDomainId, IssuingScopeSolutionId, IssuingScopeInstanceId,
            IssuingScopeTenantId, IssuingScopeCommunityId, IssuingScopeResourceId, IssuingScopeEventId) = value.Coordinates;
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

    public IReadOnlySet<ModerationCapability> Capabilities => EffectiveRestrictions.FromCapabilityMask(CapabilityMask);
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset? EndsAt { get; private set; }
    public DateTimeOffset? ReviewAt { get; private set; }
    public int RequiredApprovals { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? FindingId { get; private set; }
    public IReadOnlyList<SubjectReference> Approvals
    {
        get
        {
            var first = SubjectReference.FromOptionalCoordinates(Approval1Kind, Approval1Id, Approval1OriginKind,
                Approval1OriginTrustDomainId, Approval1OriginSolutionId, Approval1OriginInstanceId,
                Approval1OriginTenantId, Approval1OriginCommunityId, Approval1OriginResourceId, Approval1OriginEventId);
            var second = SubjectReference.FromOptionalCoordinates(Approval2Kind, Approval2Id, Approval2OriginKind,
                Approval2OriginTrustDomainId, Approval2OriginSolutionId, Approval2OriginInstanceId,
                Approval2OriginTenantId, Approval2OriginCommunityId, Approval2OriginResourceId, Approval2OriginEventId);
            if (first is null && second is not null)
            {
                throw new ArgumentException("Approval slots must preserve insertion order without gaps.");
            }

            return Array.AsReadOnly<SubjectReference>(
                first is null ? [] : second is null ? [first] : [first, second]);
        }
    }
    public ModerationDecisionState State { get; private set; }
    public long Revision { get; private set; }

    /// <summary>Checks stored shape and sequencing, not historical finding or controller authority.</summary>
    public void ValidatePersistedState()
    {
        var subject = Subject;
        var issuer = Issuer;
        var scope = Scope;
        _ = Rule;
        _ = Capabilities;
        var approvals = Approvals;
        if (Id == Guid.Empty || CaseId == Guid.Empty || FindingId == Guid.Empty
            || subject.TrustDomainId != scope.TrustDomainId || issuer.TrustDomainId != scope.TrustDomainId
            || !IssuingScope.Contains(scope)
            || RequiredApprovals is < 1 or > 2
            || (scope.Kind is ModerationScopeKind.TrustDomain or ModerationScopeKind.Solution && RequiredApprovals != 2)
            || (EndsAt is not null && EndsAt <= StartsAt)
            || (EndsAt is null && (ReviewAt is null || RequiredApprovals != 2))
            || (ReviewAt is not null && (ReviewAt <= StartsAt || (EndsAt is not null && ReviewAt > EndsAt)))
            || UpdatedAt < CreatedAt || approvals.Count > RequiredApprovals
            || approvals.Any(approval => approval.Kind != ModerationSubjectKind.Human
                || approval.TrustDomainId != scope.TrustDomainId || approval.Id == issuer.Id || approval.Id == subject.Id)
            || (approvals.Count == 2 && approvals[0].Id == approvals[1].Id))
        {
            throw new ArgumentException("The persisted decision definition or independent approvals are inconsistent.");
        }

        var shapeIsValid = State switch
        {
            ModerationDecisionState.Proposed => FindingId is null && approvals.Count == 0
                && Revision == 1 && UpdatedAt == CreatedAt,
            ModerationDecisionState.AwaitingFinding => FindingId is null && approvals.Count == 0 && Revision == 2,
            ModerationDecisionState.AwaitingApproval => FindingId is not null && approvals.Count < RequiredApprovals
                && Revision == 3 + approvals.Count,
            ModerationDecisionState.Approved => FindingId is not null && approvals.Count == RequiredApprovals
                && Revision == 3 + approvals.Count,
            ModerationDecisionState.Scheduled => FindingId is not null && approvals.Count == RequiredApprovals
                && Revision == 4 + approvals.Count && UpdatedAt < StartsAt,
            ModerationDecisionState.Active => FindingId is not null && approvals.Count == RequiredApprovals
                && Revision == 4 + approvals.Count && UpdatedAt >= StartsAt && (EndsAt is null || UpdatedAt < EndsAt),
            ModerationDecisionState.Rejected or ModerationDecisionState.Withdrawn =>
                FindingId is null ? approvals.Count == 0 && Revision is 2 or 3 : Revision == 4 + approvals.Count,
            _ => false
        };
        if (!shapeIsValid)
        {
            throw new ArgumentException("The persisted decision state, finding and revision are inconsistent.");
        }
    }

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

    /// <summary>Accepts a current host-qualified urgency finding without inventing a subject response.</summary>
    public void AcceptUrgentFinding(ModerationFinding finding, long expectedRevision, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(finding);
        RequireState(expectedRevision, now, ModerationDecisionState.Proposed);
        finding.ValidatePersistedState();
        if (finding.State != ModerationFindingState.Final
            || finding.EvidencePurpose != ModerationFindingEvidencePurpose.QualifiedUrgency
            || finding.CaseId != CaseId || finding.Subject != Subject || finding.Scope != Scope
            || finding.Rule != Rule || finding.Adjudicator.Id == Issuer.Id || finding.FinalizedAt > now)
            throw new InvalidOperationException("An urgent transition requires the exact qualified final finding.");

        var nextRevision = checked(Revision + 1);
        FindingId = finding.Id;
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
            || Approvals.Any(approval => approval.Id == controller.Id))
        {
            throw new InvalidOperationException("The controller is not qualified and independent for this approval.");
        }

        var nextRevision = checked(Revision + 1);
        if (Approval1Id is null)
        {
            Approval1Kind = controller.Kind;
            Approval1Id = controller.Id;
            (Approval1OriginKind, Approval1OriginTrustDomainId, Approval1OriginSolutionId, Approval1OriginInstanceId,
                Approval1OriginTenantId, Approval1OriginCommunityId, Approval1OriginResourceId, Approval1OriginEventId) = controller.Origin.Coordinates;
        }
        else
        {
            Approval2Kind = controller.Kind;
            Approval2Id = controller.Id;
            (Approval2OriginKind, Approval2OriginTrustDomainId, Approval2OriginSolutionId, Approval2OriginInstanceId,
                Approval2OriginTenantId, Approval2OriginCommunityId, Approval2OriginResourceId, Approval2OriginEventId) = controller.Origin.Coordinates;
        }

        State = Approvals.Count == RequiredApprovals
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
