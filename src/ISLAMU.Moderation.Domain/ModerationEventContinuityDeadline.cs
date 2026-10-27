namespace ISLAMU.Moderation.Domain;

/// <summary>
/// Timeliness of an accountable event fallback: earlier of 24 hours after the original activation
/// or two hours before event start. Missing or already overdue schedules require escalation now.
/// This value supplies neither steward consent, event permission nor settlement authority.
/// </summary>
public sealed record ModerationEventContinuityDeadline
{
    private ModerationEventContinuityDeadline(DateTimeOffset activatedAt, DateTimeOffset? eventStartsAt, DateTimeOffset dueAt)
    {
        ActivatedAt = activatedAt;
        EventStartsAt = eventStartsAt;
        DueAt = dueAt;
    }

    public DateTimeOffset ActivatedAt { get; }
    public DateTimeOffset? EventStartsAt { get; }
    public DateTimeOffset DueAt { get; }
    public bool HasKnownSchedule => EventStartsAt is not null;

    public static ModerationEventContinuityDeadline Create(DateTimeOffset activatedAt, DateTimeOffset? eventStartsAt)
    {
        if (activatedAt.Offset != TimeSpan.Zero || activatedAt == DateTimeOffset.MinValue
            || activatedAt >= DateTimeOffset.MaxValue.AddHours(-24)
            || eventStartsAt is { } start && (start.Offset != TimeSpan.Zero
                || start <= DateTimeOffset.MinValue.AddHours(2) || start == DateTimeOffset.MaxValue))
            throw new ArgumentException("Continuity requires canonical UTC and representable finite deadline boundaries.");
        var dueAt = activatedAt.AddHours(24);
        if (eventStartsAt is null)
            dueAt = activatedAt;
        else if (eventStartsAt.Value.AddHours(-2) < dueAt)
            dueAt = eventStartsAt.Value.AddHours(-2);
        return new(activatedAt, eventStartsAt, dueAt);
    }

    public bool IsDue(DateTimeOffset observedAt)
    {
        if (observedAt.Offset != TimeSpan.Zero || observedAt < ActivatedAt || observedAt == DateTimeOffset.MaxValue)
            throw new ArgumentException("Continuity observation must be canonical, finite and non-regressing.", nameof(observedAt));
        return observedAt >= DueAt;
    }
}
