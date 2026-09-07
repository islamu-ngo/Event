namespace Explore.Application.Contracts.Scheduling;

/// <summary>
/// A deadline is a pointer, never a message. <see cref="Pointer"/> is deliberately typed as a map of
/// strings rather than an object graph: scheduler rows are durable, long-lived, and outside the
/// application's own retention and privacy machinery, so anything richer would eventually carry a
/// recipient, a body, or a token into a table nothing redacts. The job re-reads the real state from the
/// database under the identifiers it finds here.
/// </summary>
/// <param name="JobName">Operational job name from <see cref="ScheduledJobNames"/> that must run.</param>
/// <param name="DeadlineKey">
/// Stable identity for this deadline within the job, normally the aggregate id. Registering the same key
/// twice replaces the pending deadline rather than adding a second one, and cancellation is by this key.
/// </param>
/// <param name="DueAt">Instant the job should run at.</param>
/// <param name="Pointer">Durable identifiers the job needs to find its work. Values must be strings.</param>
public sealed record ScheduledDeadline(
    string JobName,
    string DeadlineKey,
    DateTimeOffset DueAt,
    IReadOnlyDictionary<string, string> Pointer);

/// <summary>
/// Outcome of a deadline registration. A failure is reported rather than thrown because every caller in
/// this platform treats precise scheduling as an optimization over a reconciliation sweep — the deadline
/// makes work timely, the sweep makes it correct — so a scheduler outage must never fail a business
/// transaction.
/// </summary>
public sealed record ScheduledDeadlineResult(bool Scheduled, string FailureCategory)
{
    /// <summary>Failure category reported when the deadline was accepted.</summary>
    public const string NoFailure = "none";

    /// <summary>Failure category reported when the host runs without a scheduler at all.</summary>
    public const string SchedulerDisabled = "scheduler_disabled";

    /// <summary>Failure category reported when a scheduler exists but refused or could not be reached.</summary>
    public const string SchedulerUnavailable = "scheduler_unavailable";

    public static ScheduledDeadlineResult Success()
    {
        return new ScheduledDeadlineResult(true, NoFailure);
    }

    public static ScheduledDeadlineResult NotScheduled(string failureCategory)
    {
        return new ScheduledDeadlineResult(false, failureCategory);
    }
}
