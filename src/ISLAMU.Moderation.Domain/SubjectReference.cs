namespace ISLAMU.Moderation.Domain;

public enum ModerationSubjectKind
{
    Human = 1,
    Service = 2,
    Organization = 3,
    Content = 4
}

/// <summary>A canonical subject, not an email, provider handle or identity-linkage assertion.</summary>
public sealed record SubjectReference
{
    private SubjectReference(ModerationSubjectKind kind, Guid id, AuthorityScope origin)
    {
        Kind = kind;
        Id = id;
        Origin = origin;
    }

    public ModerationSubjectKind Kind { get; }
    public Guid Id { get; }
    public AuthorityScope Origin { get; }
    public Guid TrustDomainId => Origin.TrustDomainId;

    public static SubjectReference Create(ModerationSubjectKind kind, Guid id, AuthorityScope origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        if (!Enum.IsDefined(kind) || id == Guid.Empty)
        {
            throw new ArgumentException("A known kind and canonical subject identifier are required.", nameof(id));
        }

        return new SubjectReference(kind, id, origin);
    }

    public override string ToString() => Kind.ToString();

    internal static SubjectReference? FromOptionalCoordinates(
        ModerationSubjectKind? kind, Guid? id, ModerationScopeKind? originKind, Guid? trustDomainId,
        Guid? solutionId, Guid? instanceId, Guid? tenantId, Guid? communityId, Guid? resourceId, Guid? eventId)
    {
        if (kind is null && id is null && originKind is null && trustDomainId is null
            && solutionId is null && instanceId is null && tenantId is null && communityId is null
            && resourceId is null && eventId is null)
        {
            return null;
        }

        if (kind is null || id is null || originKind is null || trustDomainId is null)
        {
            throw new ArgumentException("An optional subject must be absent or fully qualified.", nameof(kind));
        }

        return Create(kind.Value, id.Value, AuthorityScope.FromCoordinates(
            originKind.Value, trustDomainId.Value, solutionId, instanceId, tenantId, communityId, resourceId, eventId));
    }
}
