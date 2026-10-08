namespace ISLAMU.Moderation.Domain;

public enum ModerationSanctionState
{
    Scheduled = 1,
    Active = 2,
    Expired = 3,
    Revoked = 4,
    Superseded = 5
}

/// <summary>An independent contribution whose original scope and interval never widen.</summary>
public sealed class ModerationSanction
{
    private ModerationSanction()
    {
    }

    internal ModerationSanction(Guid id, ModerationDecision decision, DateTimeOffset now)
    {
        Id = id;
        DecisionId = decision.Id;
        Subject = decision.Subject;
        Scope = decision.Scope;
        Rule = decision.Rule;
        CapabilityMask = decision.CapabilityMask;
        StartsAt = decision.StartsAt;
        EndsAt = decision.EndsAt;
        ReviewAt = decision.ReviewAt;
        CreatedAt = now.ToUniversalTime();
        UpdatedAt = CreatedAt;
        State = now < StartsAt ? ModerationSanctionState.Scheduled : ModerationSanctionState.Active;
        Revision = 1;
    }

    public Guid Id { get; private set; }
    public Guid DecisionId { get; private set; }
    public ModerationSubjectKind SubjectKind { get; private set; }
    public Guid SubjectId { get; private set; }
    public ModerationScopeKind SubjectOriginKind { get; private set; }
    public Guid SubjectOriginTrustDomainId { get; private set; }
    public Guid? SubjectOriginSolutionId { get; private set; }
    public Guid? SubjectOriginInstanceId { get; private set; }
    public Guid? SubjectOriginTenantId { get; private set; }
    public Guid? SubjectOriginCommunityId { get; private set; }
    public Guid? SubjectOriginResourceId { get; private set; }
    public ModerationScopeKind ScopeKind { get; private set; }
    public Guid ScopeTrustDomainId { get; private set; }
    public Guid? ScopeSolutionId { get; private set; }
    public Guid? ScopeInstanceId { get; private set; }
    public Guid? ScopeTenantId { get; private set; }
    public Guid? ScopeCommunityId { get; private set; }
    public Guid? ScopeResourceId { get; private set; }
    public Guid RuleOwnerId { get; private set; }
    public string RuleCode { get; private set; } = string.Empty;
    public long RuleRevision { get; private set; }
    public int CapabilityMask { get; private set; }

    public SubjectReference Subject
    {
        get => SubjectReference.Create(SubjectKind, SubjectId, AuthorityScope.FromCoordinates(
            SubjectOriginKind, SubjectOriginTrustDomainId, SubjectOriginSolutionId, SubjectOriginInstanceId,
            SubjectOriginTenantId, SubjectOriginCommunityId, SubjectOriginResourceId));
        private set
        {
            SubjectKind = value.Kind;
            SubjectId = value.Id;
            (SubjectOriginKind, SubjectOriginTrustDomainId, SubjectOriginSolutionId, SubjectOriginInstanceId,
                SubjectOriginTenantId, SubjectOriginCommunityId, SubjectOriginResourceId) = value.Origin.Coordinates;
        }
    }

    public AuthorityScope Scope
    {
        get => AuthorityScope.FromCoordinates(ScopeKind, ScopeTrustDomainId, ScopeSolutionId, ScopeInstanceId,
            ScopeTenantId, ScopeCommunityId, ScopeResourceId);
        private set => (ScopeKind, ScopeTrustDomainId, ScopeSolutionId, ScopeInstanceId,
            ScopeTenantId, ScopeCommunityId, ScopeResourceId) = value.Coordinates;
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
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? SupersedingDecisionId { get; private set; }
    public ModerationSanctionState State { get; private set; }
    public long Revision { get; private set; }

    /// <summary>Checks persisted contribution structure without reconciling time or proving the decision's authority.</summary>
    public void ValidatePersistedState()
    {
        _ = Rule;
        _ = Capabilities;
        if (Id == Guid.Empty || DecisionId == Guid.Empty || Subject.TrustDomainId != Scope.TrustDomainId
            || (EndsAt is not null && (EndsAt <= StartsAt || CreatedAt >= EndsAt))
            || (EndsAt is null && ReviewAt is null)
            || (ReviewAt is not null && (ReviewAt <= StartsAt || (EndsAt is not null && ReviewAt > EndsAt)))
            || UpdatedAt < CreatedAt
            || (State == ModerationSanctionState.Superseded) != (SupersedingDecisionId is not null)
            || SupersedingDecisionId == Guid.Empty || SupersedingDecisionId == DecisionId)
        {
            throw new ArgumentException("The persisted contribution definition or replacement identity is inconsistent.");
        }

        var initiallyScheduled = CreatedAt < StartsAt;
        var shapeIsValid = State switch
        {
            ModerationSanctionState.Scheduled => initiallyScheduled && Revision == 1 && UpdatedAt == CreatedAt,
            ModerationSanctionState.Active => UpdatedAt >= StartsAt && (EndsAt is null || UpdatedAt < EndsAt)
                && (initiallyScheduled ? Revision == 2 : Revision == 1 && UpdatedAt == CreatedAt),
            ModerationSanctionState.Expired => EndsAt is not null && UpdatedAt >= EndsAt
                && (initiallyScheduled ? Revision is 2 or 3 : Revision == 2),
            ModerationSanctionState.Revoked or ModerationSanctionState.Superseded =>
                Revision == 2
                || (Revision == 3 && (initiallyScheduled ? UpdatedAt >= StartsAt : EndsAt is not null && UpdatedAt >= EndsAt))
                || (Revision == 4 && initiallyScheduled && EndsAt is not null && UpdatedAt >= EndsAt),
            _ => false
        };
        if (!shapeIsValid)
        {
            throw new ArgumentException("The persisted contribution state, interval and revision are inconsistent.");
        }
    }

    public bool IsEffectiveAt(DateTimeOffset now) =>
        State is ModerationSanctionState.Scheduled or ModerationSanctionState.Active
        && now >= StartsAt
        && (EndsAt is null || now < EndsAt);

    public void Revoke(long expectedRevision, DateTimeOffset now)
    {
        RequireCurrentContribution(expectedRevision, now);
        var nextRevision = checked(Revision + 1);
        State = ModerationSanctionState.Revoked;
        UpdatedAt = now.ToUniversalTime();
        Revision = nextRevision;
    }

    public void Supersede(Guid decisionId, long expectedRevision, DateTimeOffset now)
    {
        RequireCurrentContribution(expectedRevision, now);
        if (decisionId == Guid.Empty || decisionId == DecisionId)
        {
            throw new ArgumentException("A distinct qualified replacement decision is required.", nameof(decisionId));
        }

        var nextRevision = checked(Revision + 1);
        SupersedingDecisionId = decisionId;
        State = ModerationSanctionState.Superseded;
        UpdatedAt = now.ToUniversalTime();
        Revision = nextRevision;
    }

    /// <summary>Materializes time-based state for reconciliation; effective expiry never depends on this call.</summary>
    public bool Reconcile(long expectedRevision, DateTimeOffset now)
    {
        if (expectedRevision != Revision || now < UpdatedAt)
        {
            throw new InvalidOperationException("The contribution revision or transition instant is not current.");
        }

        if (State is not (ModerationSanctionState.Scheduled or ModerationSanctionState.Active))
        {
            return false;
        }

        var state = EndsAt is not null && now >= EndsAt
            ? ModerationSanctionState.Expired
            : now >= StartsAt ? ModerationSanctionState.Active : ModerationSanctionState.Scheduled;
        if (state == State)
        {
            return false;
        }

        var nextRevision = checked(Revision + 1);
        State = state;
        UpdatedAt = now.ToUniversalTime();
        Revision = nextRevision;
        return true;
    }

    private void RequireCurrentContribution(long expectedRevision, DateTimeOffset now)
    {
        if (expectedRevision != Revision || now < UpdatedAt
            || State is ModerationSanctionState.Revoked or ModerationSanctionState.Superseded)
        {
            throw new InvalidOperationException("The contribution is no longer current for correction.");
        }
    }
}
