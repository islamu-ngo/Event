using ISLAMU.Moderation.Domain;

namespace Explore.Application.Contracts.Services;

/// <summary>
/// Reads current adopted resource protection and canonical requester provenance.
/// Installation grants no permission. Adopted protection requires current provenance;
/// support for activating new effects remains separate from existing restrictions.
/// </summary>
public interface IEventResourceModerationSnapshotReader
{
    Task<EventResourceModerationFacts> ReadAsync(
        EventResourceAuthorityRequest request, DateTimeOffset evaluationUtc,
        CancellationToken cancellationToken);

    /// <summary>Reads one requester's bounded targets by source category without per-resource queries.</summary>
    Task<IReadOnlyList<EventResourceModerationFacts>> ReadBatchAsync(
        IReadOnlyList<EventResourceAuthorityRequest> requests, DateTimeOffset evaluationUtc,
        CancellationToken cancellationToken);
}

/// <summary>
/// Immutable moderation overlay facts compared across private preparation and delivery.
/// Generation binds the applicable policy, qualification and contribution revisions.
/// </summary>
public sealed record EventResourceModerationFacts
{
    private EventResourceModerationFacts(
        bool isAdopted, SubjectReference? subject, ModerationRestrictionOutcome outcome,
        string generation, Guid? bindingId, long? bindingRevision, DateTimeOffset? currentUntil)
    {
        IsAdopted = isAdopted;
        Subject = subject;
        Outcome = outcome;
        Generation = generation;
        BindingId = bindingId;
        BindingRevision = bindingRevision;
        CurrentUntil = currentUntil;
    }

    public bool IsAdopted { get; }
    public SubjectReference? Subject { get; }
    public ModerationRestrictionOutcome Outcome { get; }
    public string Generation { get; }
    public Guid? BindingId { get; }
    public long? BindingRevision { get; }
    public DateTimeOffset? CurrentUntil { get; }
    public bool PermitsNativeEvaluationAt(DateTimeOffset now) => now.Offset == TimeSpan.Zero
        && (!IsAdopted || Subject is not null && Outcome == ModerationRestrictionOutcome.Unrestricted
            && (CurrentUntil is null || now < CurrentUntil));

    public static EventResourceModerationFacts NotAdopted { get; } =
        new(false, null, ModerationRestrictionOutcome.Unrestricted, "not-adopted", null, null, null);

    public static EventResourceModerationFacts QualifiedNonadoption(string generation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(generation);
        return new(false, null, ModerationRestrictionOutcome.Unrestricted, generation, null, null, null);
    }

    public static EventResourceModerationFacts Unavailable(string generation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(generation);
        return new(true, null, ModerationRestrictionOutcome.Unavailable, generation, null, null, null);
    }

    public static EventResourceModerationFacts Qualified(
        SubjectReference subject, ModerationRestrictionOutcome outcome, string generation,
        Guid? bindingId = null, long? bindingRevision = null, DateTimeOffset? currentUntil = null)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(generation);
        if (!Enum.IsDefined(outcome) || bindingId == Guid.Empty || bindingRevision < 1
            || bindingId.HasValue != bindingRevision.HasValue
            || subject.Kind == ModerationSubjectKind.Service && bindingId is null
            || subject.Kind != ModerationSubjectKind.Service && bindingId is not null
            || currentUntil is { } boundary && (boundary.Offset != TimeSpan.Zero
                || boundary == DateTimeOffset.MinValue || boundary == DateTimeOffset.MaxValue))
            throw new ArgumentException("Qualified moderation requires exact subject binding and any lifetime boundary must be finite UTC.");
        return new(true, subject, outcome, generation, bindingId, bindingRevision, currentUntil);
    }
}
