namespace Explore.Domain;

/// <summary>Exact native generation or unversioned grant identity. GUID stamps are never truncated into counters.</summary>
public sealed record ModerationCaseRelationshipGeneration
{
    private ModerationCaseRelationshipGeneration(Guid? stamp, long? revision, Guid? identity = null)
    {
        ConcurrencyStamp = stamp;
        Revision = revision;
        Identity = identity;
    }

    public Guid? ConcurrencyStamp { get; }
    public long? Revision { get; }
    public Guid? Identity { get; }

    public static ModerationCaseRelationshipGeneration FromStamp(Guid stamp) =>
        stamp != Guid.Empty ? new(stamp, null) : throw new ArgumentException("A native concurrency stamp is required.");

    public static ModerationCaseRelationshipGeneration FromRevision(long revision) =>
        revision > 0 ? new(null, revision) : throw new ArgumentException("A positive native revision is required.");

    /// <summary>Platform role grants have a native row identity, not a revision or concurrency stamp.</summary>
    public static ModerationCaseRelationshipGeneration FromIdentity(Guid identity) =>
        identity != Guid.Empty ? new(null, null, identity) : throw new ArgumentException("A native grant identity is required.");
}

public enum ModerationCaseRelationshipKind
{
    EventSubmitter = 1,
    EventRole = 2,
    RegistrationOrder = 3,
    RegistrationParticipant = 4,
    CaseReadGrant = 5,
    PublicEvent = 6,
    InstanceAdministrator = 7
}
