namespace Explore.Domain;

/// <summary>A qualified committed change awaiting native consequence planning; delivery and evidence custody are separate.</summary>
public sealed class ModerationEffectIntent
{
    private ModerationEffectIntent()
    {
    }

    public Guid Id { get; private set; }
    public Guid OperationId { get; private set; }
    public Guid DecisionId { get; private set; }
    public long DecisionRevision { get; private set; }
    public Guid? SanctionId { get; private set; }
    public long? SanctionRevision { get; private set; }
    public Guid? SupersededSanctionId { get; private set; }
    public long? SupersededSanctionRevision { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public int ScopeKind { get; private set; }
    public Guid ScopeId { get; private set; }
    public int SubjectKind { get; private set; }
    public Guid SubjectId { get; private set; }
    public int OperationKind { get; private set; }
    public long PolicyRevision { get; private set; }
    public long QualificationRevision { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public long Revision { get; private set; }

    public static ModerationEffectIntent Create(
        Guid id, Guid operationId, Guid decisionId, long decisionRevision, Guid? sanctionId, long? sanctionRevision,
        Guid trustDomainId, int scopeKind, Guid scopeId, int subjectKind, Guid subjectId, int operationKind,
        long policyRevision, long qualificationRevision, DateTimeOffset now,
        Guid? supersededSanctionId, long? supersededSanctionRevision)
    {
        if (id == Guid.Empty || operationId == Guid.Empty || decisionId == Guid.Empty
            || trustDomainId == Guid.Empty || scopeId == Guid.Empty || subjectId == Guid.Empty
            || decisionRevision < 1 || policyRevision < 1 || qualificationRevision < 1
            || sanctionId == Guid.Empty || (sanctionId is null) != (sanctionRevision is null)
            || (sanctionRevision is not null && sanctionRevision < 1)
            || supersededSanctionId == Guid.Empty || (supersededSanctionId is null) != (supersededSanctionRevision is null)
            || (supersededSanctionRevision is not null && supersededSanctionRevision < 1)
            || (operationKind == 10) != (supersededSanctionId is not null)
            || supersededSanctionId is not null && (sanctionId is null || supersededSanctionId == sanctionId)
            || scopeKind is < 1 or > 6 || subjectKind is < 1 or > 4 || operationKind is < 1 or > 10)
        {
            throw new ArgumentException("A durable effect requires an exact qualified changed aggregate.", nameof(id));
        }

        return new ModerationEffectIntent
        {
            Id = id,
            OperationId = operationId,
            DecisionId = decisionId,
            DecisionRevision = decisionRevision,
            SanctionId = sanctionId,
            SanctionRevision = sanctionRevision,
            SupersededSanctionId = supersededSanctionId,
            SupersededSanctionRevision = supersededSanctionRevision,
            TrustDomainId = trustDomainId,
            ScopeKind = scopeKind,
            ScopeId = scopeId,
            SubjectKind = subjectKind,
            SubjectId = subjectId,
            OperationKind = operationKind,
            PolicyRevision = policyRevision,
            QualificationRevision = qualificationRevision,
            CreatedAt = now.ToUniversalTime(),
            Revision = 1
        };
    }

    public void Complete(long expectedRevision, DateTimeOffset now)
    {
        if (Revision != expectedRevision || CompletedAt is not null || now < CreatedAt)
        {
            throw new InvalidOperationException("The effect intent is no longer current.");
        }

        var nextRevision = checked(Revision + 1);
        CompletedAt = now.ToUniversalTime();
        Revision = nextRevision;
    }

    public override string ToString() => nameof(ModerationEffectIntent);
}
