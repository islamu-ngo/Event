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
    internal ModerationSanction(Guid id, ModerationDecision decision, DateTimeOffset now)
    {
        Id = id;
        DecisionId = decision.Id;
        Subject = decision.Subject;
        Scope = decision.Scope;
        Rule = decision.Rule;
        Capabilities = decision.Capabilities;
        StartsAt = decision.StartsAt;
        EndsAt = decision.EndsAt;
        ReviewAt = decision.ReviewAt;
        CreatedAt = now.ToUniversalTime();
        UpdatedAt = CreatedAt;
        State = now < StartsAt ? ModerationSanctionState.Scheduled : ModerationSanctionState.Active;
    }

    public Guid Id { get; }
    public Guid DecisionId { get; }
    public SubjectReference Subject { get; }
    public AuthorityScope Scope { get; }
    public ModerationRuleReference Rule { get; }
    public IReadOnlySet<ModerationCapability> Capabilities { get; }
    public DateTimeOffset StartsAt { get; }
    public DateTimeOffset? EndsAt { get; }
    public DateTimeOffset? ReviewAt { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? SupersedingDecisionId { get; private set; }
    public ModerationSanctionState State { get; private set; }
    public long Revision { get; private set; } = 1;

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
