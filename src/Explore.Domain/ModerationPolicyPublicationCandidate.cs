using ISLAMU.Moderation.Domain;

namespace Explore.Domain;

/// <summary>
/// Immutable preparation of a future publication, not evidence, permission or an active selection.
/// The definition retains its own inactive snapshot identity; Id is reserved for the final writer,
/// which must independently qualify the exact proposed active state and original source authority.
/// </summary>
public sealed class ModerationPolicyPublicationCandidate
{
    private ModerationPolicyPublicationCandidate() { }

    public Guid Id { get; private set; }
    public Guid DefinitionId { get; private set; }
    public Guid PolicyId { get; private set; }
    public long ExpectedRevision { get; private set; }
    public Guid? PreviousPublicationId { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid SolutionId { get; private set; }
    public Guid InstanceId { get; private set; }
    public Guid? JurisdictionTenantId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid PreparedByHumanId { get; private set; }
    public Guid OperationId { get; private set; }
    public string RequestDigest { get; private set; } = string.Empty;
    public string ProposedStateDigest { get; private set; } = string.Empty;
    public Guid AuthoritySourceId { get; private set; }
    public long AuthorityRevision { get; private set; }
    public long WriterGeneration { get; private set; }
    public DateTimeOffset PreparedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }
    public long Revision { get; private set; }
    public ModerationPolicyPublication? Definition { get; private set; }

    public AuthorityScope Jurisdiction
    {
        get
        {
            var instance = AuthorityScope.Instance(
                AuthorityScope.Solution(AuthorityScope.TrustDomain(TrustDomainId), SolutionId), InstanceId);
            return JurisdictionTenantId is { } tenant ? AuthorityScope.Tenant(instance, tenant) : instance;
        }
    }

    /// <summary>
    /// Freezes the original snapshot's owner, ancestry, predecessor and authority coordinates.
    /// ProposedStateDigest binds the exact intended active state using the reserved future Id,
    /// not the inactive definition's declared-state digest. Preparation never qualifies that state.
    /// </summary>
    public static ModerationPolicyPublicationCandidate Prepare(
        ModerationPolicyPublication snapshot, Guid publicationId, Guid operationId,
        string requestDigest, string proposedStateDigest, Guid humanId,
        DateTimeOffset now, DateTimeOffset validUntil)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.AdoptedRevision is not null || snapshot.PublishedActive
            || snapshot.Evidence.Count != 0 || snapshot.EvidenceValidUntil is not null
            || snapshot.PrincipalId != humanId || snapshot.PublishedAt != now
            || snapshot.PublishedAt.Offset != TimeSpan.Zero || snapshot.OperationId != operationId
            || snapshot.RequestDigest != requestDigest || snapshot.Revision < 1
            || snapshot.AuthorityRevision is < 1 or >= long.MaxValue - 1
            || snapshot.QualificationRevision != snapshot.AuthorityRevision + 1)
            throw new ArgumentException("Preparation requires the exact original inactive, evidence-free human snapshot and source generation.");

        var candidate = new ModerationPolicyPublicationCandidate
        {
            Id = publicationId, DefinitionId = snapshot.Id, PolicyId = snapshot.PolicyId,
            ExpectedRevision = snapshot.Revision - 1, PreviousPublicationId = snapshot.PreviousPublicationId,
            HostBindingId = snapshot.HostBindingId, TrustDomainId = snapshot.TrustDomainId,
            SolutionId = snapshot.SolutionId, InstanceId = snapshot.InstanceId,
            JurisdictionTenantId = snapshot.JurisdictionTenantId, TenantId = snapshot.TenantId,
            PreparedByHumanId = humanId, OperationId = operationId, RequestDigest = requestDigest,
            ProposedStateDigest = proposedStateDigest, AuthoritySourceId = snapshot.AuthoritySourceId,
            AuthorityRevision = snapshot.AuthorityRevision, WriterGeneration = snapshot.QualificationRevision,
            PreparedAt = now, ValidUntil = validUntil, Revision = 1, Definition = snapshot
        };
        candidate.ValidatePersistedState();
        return candidate;
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, DefinitionId, PolicyId, HostBindingId, TrustDomainId,
            SolutionId, InstanceId, TenantId, PreparedByHumanId, OperationId, AuthoritySourceId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        if (PreviousPublicationId is { } previous)
            ModerationHostBinding.RequireUuidV7(previous, nameof(PreviousPublicationId));
        if (JurisdictionTenantId is { } tenant)
            ModerationHostBinding.RequireUuidV7(tenant, nameof(JurisdictionTenantId));
        if (Id == DefinitionId || ExpectedRevision is < 0 or long.MaxValue
            || (PreviousPublicationId is null) != (ExpectedRevision == 0)
            || AuthorityRevision is < 1 or >= long.MaxValue - 1
            || WriterGeneration != AuthorityRevision + 1 || Revision != 1
            || !Digest(RequestDigest) || !Digest(ProposedStateDigest))
            throw new ArgumentException("A candidate requires distinct reserved identity, exact predecessor and original finite source coordinates.");
        ModerationQualificationPolicySource.RequireValidity(PreparedAt, ValidUntil);
        if (Definition is { } definition
            && (definition.Id != DefinitionId || definition.PolicyId != PolicyId
                || definition.Revision - 1 != ExpectedRevision || definition.PreviousPublicationId != PreviousPublicationId
                || definition.HostBindingId != HostBindingId || definition.TrustDomainId != TrustDomainId
                || definition.SolutionId != SolutionId || definition.InstanceId != InstanceId
                || definition.JurisdictionTenantId != JurisdictionTenantId || definition.TenantId != TenantId
                || definition.PrincipalId != PreparedByHumanId || definition.OperationId != OperationId
                || definition.RequestDigest != RequestDigest || definition.PublishedAt != PreparedAt
                || definition.PublishedAt.Offset != TimeSpan.Zero || definition.AdoptedRevision is not null
                || definition.PublishedActive
                || definition.Evidence.Count != 0 || definition.EvidenceValidUntil is not null
                || definition.AuthoritySourceId != AuthoritySourceId || definition.AuthorityRevision != AuthorityRevision
                || definition.QualificationRevision != WriterGeneration))
            throw new ArgumentException("The retained definition must match the original evidence-free preparation exactly.");
    }

    private static bool Digest(string? value) => value is { Length: 64 }
        && value.All(char.IsAsciiHexDigit)
        && (value.All(c => c is not (>= 'a' and <= 'f'))
            || value.All(c => c is not (>= 'A' and <= 'F')));

    public override string ToString() => nameof(ModerationPolicyPublicationCandidate);
}
