namespace ISLAMU.Moderation.Domain;

public enum ModerationCaseTargetKind
{
    EventVersion = 1,
    ParticipantIncident = 2,
    Actor = 3,
    Service = 4,
    Resource = 5,
    Form = 6,
    Destination = 7,
    HiddenContentReceipt = 8,
    LocalIncident = 9
}

/// <summary>
/// Identifies the native object an allegation concerns, not a responsible human or an adjudicated
/// subject. Construction checks coordinates only; a native owner must establish existence,
/// current version, caller relationship and case-purpose authority without releasing the object.
/// </summary>
public sealed record ModerationCaseTarget
{
    private ModerationCaseTarget(
        ModerationCaseTargetKind kind, Guid id, AuthorityScope origin, Guid version)
    {
        Kind = kind;
        Id = id;
        Origin = origin;
        Version = version;
    }

    public ModerationCaseTargetKind Kind { get; }
    public Guid Id { get; }
    public AuthorityScope Origin { get; }

    /// <summary>The exact native source generation, never a timestamp or a claimant's attribution.</summary>
    public Guid Version { get; }

    /// <summary>
    /// Actor identifies the publisher row, not its user, organization or service principal.
    /// Destination identifies the resource owning the protected destination at this version.
    /// ParticipantIncident and LocalIncident have no implicit global subject.
    /// HiddenContentReceipt identifies a receipt owner; it does not authenticate a receipt
    /// or assert access to the hidden target.
    /// </summary>
    public static ModerationCaseTarget Create(
        ModerationCaseTargetKind kind, Guid id, AuthorityScope origin, Guid version)
    {
        ArgumentNullException.ThrowIfNull(origin);
        if (!Enum.IsDefined(kind) || id == Guid.Empty || version == Guid.Empty)
            throw new ArgumentException("A known target kind, native identifier and version are required.", nameof(id));

        var requiredOrigin = kind is ModerationCaseTargetKind.Actor or ModerationCaseTargetKind.Service
            ? ModerationScopeKind.Instance : ModerationScopeKind.Tenant;
        if (origin.Kind != requiredOrigin)
            throw new ArgumentException("The target origin must retain its native ownership boundary.", nameof(origin));

        return new(kind, id, origin, version);
    }

    public override string ToString() => nameof(ModerationCaseTarget);
}
