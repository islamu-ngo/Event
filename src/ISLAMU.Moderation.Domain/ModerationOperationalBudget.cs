namespace ISLAMU.Moderation.Domain;

public enum ModerationOperationalAction
{
    SubmitReport = 1,
    SubmitContestation = 2,
    CreateEvent = 3,
    PublishEvent = 4,
    ImportEvent = 5,
    ServiceEffect = 6
}

public enum ModerationOperationalBudgetState
{
    Available = 1,
    Exhausted = 2,
    Duplicate = 3
}

/// <summary>Original occurrence metadata, not a network fingerprint, service credential or finding.</summary>
public sealed record ModerationOperationalOccurrence(
    Guid Id, SubjectReference Subject, AuthorityScope Scope, Guid? MandateId,
    ModerationOperationalAction Action, DateTimeOffset RecordedAt);

/// <summary>Capacity accounting only; even Available supplies no native action permission.</summary>
public sealed record ModerationOperationalBudgetAssessment(
    ModerationOperationalBudgetState State, int DistinctOccurrences);

/// <summary>
/// Explicit rolling-window terms for one canonical subject, scope, mandate and action.
/// Native owners must independently qualify the operator terms and current mandate, read
/// complete occurrence history and serialize admission with the owning operation.
/// Policy revision or credential rotation must not replace that occurrence history.
/// </summary>
public sealed class ModerationOperationalBudget
{
    public ModerationOperationalBudget(
        SubjectReference subject, AuthorityScope scope, Guid? mandateId,
        ModerationOperationalAction action, int maximumOccurrences, TimeSpan window)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(scope);
        if (subject.Kind == ModerationSubjectKind.Content || subject.TrustDomainId != scope.TrustDomainId
            || !Enum.IsDefined(action) || maximumOccurrences < 1 || window <= TimeSpan.Zero
            || mandateId == Guid.Empty || subject.Kind == ModerationSubjectKind.Service && mandateId is null)
            throw new ArgumentException("Operational accounting requires explicit finite terms and an actual acting subject/mandate.");
        Subject = subject;
        Scope = scope;
        MandateId = mandateId;
        Action = action;
        MaximumOccurrences = maximumOccurrences;
        Window = window;
    }

    public SubjectReference Subject { get; }
    public AuthorityScope Scope { get; }
    public Guid? MandateId { get; }
    public ModerationOperationalAction Action { get; }
    public int MaximumOccurrences { get; }
    public TimeSpan Window { get; }

    public ModerationOperationalBudgetAssessment Assess(
        Guid occurrenceId, DateTimeOffset observedAt, IEnumerable<ModerationOperationalOccurrence> history)
    {
        ArgumentNullException.ThrowIfNull(history);
        RequireOccurrenceId(occurrenceId);
        if (observedAt.Offset != TimeSpan.Zero || observedAt == DateTimeOffset.MaxValue
            || observedAt.UtcTicks <= Window.Ticks)
            throw new ArgumentException("Budget observation requires a finite UTC clock and representable rolling window.");
        var start = observedAt.Subtract(Window);
        var original = new Dictionary<Guid, DateTimeOffset>();
        foreach (var occurrence in history)
        {
            if (occurrence.Subject != Subject || occurrence.Scope != Scope || occurrence.MandateId != MandateId
                || occurrence.Action != Action)
                continue;
            RequireOccurrenceId(occurrence.Id);
            if (occurrence.RecordedAt.Offset != TimeSpan.Zero || occurrence.RecordedAt == DateTimeOffset.MinValue
                || occurrence.RecordedAt > observedAt
                || original.TryGetValue(occurrence.Id, out var recordedAt) && recordedAt != occurrence.RecordedAt)
                throw new ArgumentException("An occurrence must retain one original, nonfuture UTC timestamp.");
            original[occurrence.Id] = occurrence.RecordedAt;
        }
        var count = original.Values.Count(at => at >= start);
        return new(original.ContainsKey(occurrenceId) ? ModerationOperationalBudgetState.Duplicate
            : count >= MaximumOccurrences ? ModerationOperationalBudgetState.Exhausted
            : ModerationOperationalBudgetState.Available, count);
    }

    private static void RequireOccurrenceId(Guid id)
    {
        if (id == Guid.Empty || id.Version != 7 || id.Variant is < 8 or > 11)
            throw new ArgumentException("Operational occurrence provenance requires a UUIDv7 identity.", nameof(id));
    }
}
