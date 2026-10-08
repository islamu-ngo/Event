namespace Explore.Domain;

/// <summary>Exact accepted local mutation evidence; never a reusable permission or a container for custody evidence.</summary>
public sealed class ModerationOperationReceipt
{
    private ModerationOperationReceipt()
    {
    }

    public Guid Id { get; private set; }
    public string RequestDigest { get; private set; } = string.Empty;
    public Guid TrustDomainId { get; private set; }
    public int ScopeKind { get; private set; }
    public Guid ScopeId { get; private set; }
    public int SubjectKind { get; private set; }
    public Guid SubjectId { get; private set; }
    public Guid PrincipalId { get; private set; }
    public string PrincipalDigest { get; private set; } = string.Empty;
    public Guid DecisionId { get; private set; }
    public Guid? SanctionId { get; private set; }
    public long ResultRevision { get; private set; }
    public long PolicyRevision { get; private set; }
    public long QualificationRevision { get; private set; }
    public DateTimeOffset CompletedAt { get; private set; }

    public static ModerationOperationReceipt Complete(
        Guid operationId, string requestDigest, Guid trustDomainId, int scopeKind, Guid scopeId,
        int subjectKind, Guid subjectId, Guid principalId, string principalDigest, Guid decisionId, Guid? sanctionId,
        long resultRevision, long policyRevision, long qualificationRevision, DateTimeOffset completedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestDigest);
        ArgumentException.ThrowIfNullOrWhiteSpace(principalDigest);
        if (operationId == Guid.Empty || trustDomainId == Guid.Empty || scopeId == Guid.Empty
            || subjectId == Guid.Empty || principalId == Guid.Empty || decisionId == Guid.Empty
            || sanctionId == Guid.Empty || scopeKind is < 1 or > 6 || subjectKind is < 1 or > 4
            || requestDigest.Length != 64 || !requestDigest.All(char.IsAsciiHexDigit)
            || principalDigest.Length != 64 || !principalDigest.All(char.IsAsciiHexDigit)
            || resultRevision < 1 || policyRevision < 1 || qualificationRevision < 1)
        {
            throw new ArgumentException("A receipt requires an exact qualified accepted outcome.", nameof(operationId));
        }

        return new ModerationOperationReceipt
        {
            Id = operationId,
            RequestDigest = requestDigest,
            TrustDomainId = trustDomainId,
            ScopeKind = scopeKind,
            ScopeId = scopeId,
            SubjectKind = subjectKind,
            SubjectId = subjectId,
            PrincipalId = principalId,
            PrincipalDigest = principalDigest,
            DecisionId = decisionId,
            SanctionId = sanctionId,
            ResultRevision = resultRevision,
            PolicyRevision = policyRevision,
            QualificationRevision = qualificationRevision,
            CompletedAt = completedAt.ToUniversalTime()
        };
    }

    public override string ToString() => nameof(ModerationOperationReceipt);
}
