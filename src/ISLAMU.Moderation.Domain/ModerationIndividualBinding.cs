namespace ISLAMU.Moderation.Domain;

public enum ModerationIndividualTargetKind { Human = 1, RegistrationSession = 2 }
public enum ModerationIndividualPurpose
{
    IdentityEnrollment = 1,
    ProfileCompletion = 2,
    ParticipantCollection = 3,
    AudienceThreshold = 4
}

public enum ModerationIndividualRecordState
{
    Current = 1,
    Corrected = 2,
    Withdrawn = 3,
    Expired = 4,
    Erased = 5
}

/// <summary>
/// A canonical native Human or one actual individual's slot in a registration session.
/// Session and individual identifiers are native opaque coordinates, never bearer tokens,
/// order/purchaser identities or invented user accounts. Linking a guest to a Human requires
/// new native binding and evidence; this value cannot perform identity resolution.
/// </summary>
public sealed record ModerationIndividualTarget
{
    private ModerationIndividualTarget(ModerationIndividualTargetKind kind, AuthorityScope origin,
        SubjectReference? humanSubject, Guid? sessionId, Guid? individualId)
    {
        Kind = kind;
        Origin = origin;
        HumanSubject = humanSubject;
        SessionId = sessionId;
        IndividualId = individualId;
    }

    public ModerationIndividualTargetKind Kind { get; }
    public AuthorityScope Origin { get; }
    public SubjectReference? HumanSubject { get; }
    public Guid? SessionId { get; }
    public Guid? IndividualId { get; }

    public static ModerationIndividualTarget Human(SubjectReference subject)
    {
        RequireHuman(subject);
        return new(ModerationIndividualTargetKind.Human, subject.Origin, subject, null, null);
    }

    public static ModerationIndividualTarget RegistrationSession(
        AuthorityScope origin, Guid sessionId, Guid individualId)
    {
        ArgumentNullException.ThrowIfNull(origin);
        if (origin.Kind is not (ModerationScopeKind.Instance or ModerationScopeKind.Tenant))
            throw new ArgumentException("An individual session requires a native instance or tenant origin.", nameof(origin));
        ModerationPolicy.RequireVersion7(sessionId, nameof(sessionId));
        ModerationPolicy.RequireVersion7(individualId, nameof(individualId));
        return new(ModerationIndividualTargetKind.RegistrationSession, origin, null, sessionId, individualId);
    }

    internal static void RequireHuman(SubjectReference subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (subject.Kind != ModerationSubjectKind.Human || subject.Origin.Kind != ModerationScopeKind.Instance)
            throw new ArgumentException("A canonical native Human requires its instance origin.", nameof(subject));
    }

    public override string ToString() => Kind.ToString();
}

/// <summary>
/// Exact individual, accepted policy, finite purpose and native source observation coordinates.
/// A stamp identifies an observation; its sequence is a separate ordered generation, not a Guid.
/// Constructing or matching this metadata proves neither native source/adoption nor consent.
/// Native owners must authenticate the complete source publication/profile/mode and current
/// adoption behind SourceId before supplying this binding, and retain their fences through use.
/// </summary>
public sealed record ModerationIndividualBinding
{
    public ModerationIndividualBinding(
        ModerationIndividualTarget target, ModerationAgePolicy policy, ModerationIndividualPurpose purpose,
        Guid sourceId, long sourceRevision, long sourceGeneration, Guid observationStamp,
        long observationSequence, DateTimeOffset observedAt, DateTimeOffset validUntil)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(policy);
        ModerationPolicy.RequireVersion7(sourceId, nameof(sourceId));
        ModerationIndividualValidity.RequireWindow(observedAt, validUntil);
        if (!Enum.IsDefined(purpose) || observationStamp == Guid.Empty
            || sourceRevision is < 1 or long.MaxValue || sourceGeneration is < 1 or long.MaxValue
            || observationSequence is < 1 or long.MaxValue
            || !(target.Origin.Contains(policy.Jurisdiction) || policy.Jurisdiction.Contains(target.Origin)))
            throw new ArgumentException("Binding requires exact compatible scope, purpose and positive native generations.");
        Target = target;
        PolicyId = policy.PolicyId;
        PolicyRevision = policy.Revision;
        Jurisdiction = policy.Jurisdiction;
        Purpose = purpose;
        SourceId = sourceId;
        SourceRevision = sourceRevision;
        SourceGeneration = sourceGeneration;
        ObservationStamp = observationStamp;
        ObservationSequence = observationSequence;
        ObservedAt = observedAt;
        ValidUntil = validUntil;
    }

    public ModerationIndividualTarget Target { get; }
    public Guid PolicyId { get; }
    public long PolicyRevision { get; }
    public AuthorityScope Jurisdiction { get; }
    public ModerationIndividualPurpose Purpose { get; }
    public Guid SourceId { get; }
    public long SourceRevision { get; }
    public long SourceGeneration { get; }
    public Guid ObservationStamp { get; }
    public long ObservationSequence { get; }
    public DateTimeOffset ObservedAt { get; }
    public DateTimeOffset ValidUntil { get; }

    public override string ToString() => nameof(ModerationIndividualBinding);
}

/// <summary>
/// A minimized native record reference, not its private evidence or proof of qualification.
/// Its semantic role and exact individual/purpose binding are supplied by the containing contract.
/// Native owners resolve and fence these coordinates; no string, token or evidence digest belongs here.
/// </summary>
public sealed record ModerationIndividualNativeReference
{
    public ModerationIndividualNativeReference(Guid id, long revision, long generation,
        DateTimeOffset observedAt, DateTimeOffset validUntil)
    {
        ModerationPolicy.RequireVersion7(id, nameof(id));
        ModerationIndividualValidity.RequireWindow(observedAt, validUntil);
        if (revision is < 1 or long.MaxValue || generation is < 1 or long.MaxValue)
            throw new ArgumentException("A native reference requires finite positive revision and generation.");
        Id = id;
        Revision = revision;
        Generation = generation;
        ObservedAt = observedAt;
        ValidUntil = validUntil;
    }

    public Guid Id { get; }
    public long Revision { get; }
    public long Generation { get; }
    public DateTimeOffset ObservedAt { get; }
    public DateTimeOffset ValidUntil { get; }

    internal bool Covers(DateTimeOffset start, DateTimeOffset end) => ObservedAt <= start && end <= ValidUntil;
    public override string ToString() => nameof(ModerationIndividualNativeReference);
}

internal static class ModerationIndividualValidity
{
    internal static bool IsInstant(DateTimeOffset value) =>
        value.Offset == TimeSpan.Zero && value != DateTimeOffset.MinValue && value != DateTimeOffset.MaxValue;

    internal static void RequireWindow(DateTimeOffset start, DateTimeOffset end)
    {
        if (!IsInstant(start) || !IsInstant(end) || end <= start)
            throw new ArgumentException("Individual evidence requires an explicit finite half-open UTC window.");
    }

    internal static bool Contains(DateTimeOffset start, DateTimeOffset end, DateTimeOffset now) =>
        IsInstant(now) && start <= now && now < end;

    internal static void RequireRetirement(
        ModerationIndividualRecordState current, ModerationIndividualRecordState next,
        long revision, long expectedRevision, DateTimeOffset updatedAt, DateTimeOffset validUntil,
        DateTimeOffset now)
    {
        if (revision != expectedRevision || revision == long.MaxValue || !IsInstant(now) || now < updatedAt
            || next is not (ModerationIndividualRecordState.Corrected or ModerationIndividualRecordState.Withdrawn
                or ModerationIndividualRecordState.Expired or ModerationIndividualRecordState.Erased)
            || current != ModerationIndividualRecordState.Current
                && (next != ModerationIndividualRecordState.Erased
                    || current is not (ModerationIndividualRecordState.Corrected
                        or ModerationIndividualRecordState.Withdrawn or ModerationIndividualRecordState.Expired))
            || next == ModerationIndividualRecordState.Expired && now < validUntil)
            throw new InvalidOperationException("Retirement requires a current revision and monotonic terminal transition.");
    }
}
