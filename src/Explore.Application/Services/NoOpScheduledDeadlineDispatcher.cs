using Explore.Application.Contracts.Scheduling;

namespace Explore.Application.Services;

/// <summary>
/// A scheduler-less host is a supported deployment, not a broken one, so the absence of a scheduler must be
/// an ordinary answer rather than an exception. Reporting <c>scheduler_disabled</c> keeps the caller's
/// telemetry honest about why work will arrive on sweep latency instead of at its deadline.
/// </summary>
public sealed class NoOpScheduledDeadlineDispatcher : IScheduledDeadlineDispatcher
{
    public Task<ScheduledDeadlineResult> ScheduleAsync(
        ScheduledDeadline deadline,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(
            ScheduledDeadlineResult.NotScheduled(ScheduledDeadlineResult.SchedulerDisabled));
    }

    public Task<bool> CancelAsync(string jobName, string deadlineKey, CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }
}
