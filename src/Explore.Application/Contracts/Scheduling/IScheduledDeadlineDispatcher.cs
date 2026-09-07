namespace Explore.Application.Contracts.Scheduling;

/// <summary>
/// One port for every deadline in the platform. It replaced a single-purpose email trigger because the
/// deadline shape is identical everywhere — a job name, an identity, an instant, and pointer identifiers —
/// while a port per feature would duplicate key generation, payload handling, cancellation, and failure
/// classification once per feature and let them drift apart.
/// <para>
/// Implementations must be non-throwing for infrastructure faults: callers register deadlines inside
/// business transactions whose correctness rests on a reconciliation sweep, not on the deadline.
/// </para>
/// </summary>
public interface IScheduledDeadlineDispatcher
{
    /// <summary>
    /// Registers <paramref name="deadline"/>, replacing any pending deadline with the same job name and
    /// deadline key so a re-registration cannot accumulate duplicate wake-ups.
    /// </summary>
    Task<ScheduledDeadlineResult> ScheduleAsync(ScheduledDeadline deadline, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a pending deadline once its work can no longer be needed, which is what keeps scheduler
    /// state bounded rather than accumulating a dead wake-up per completed aggregate.
    /// </summary>
    /// <returns><c>true</c> when a pending deadline existed and was removed; otherwise <c>false</c>.</returns>
    Task<bool> CancelAsync(string jobName, string deadlineKey, CancellationToken cancellationToken);
}
