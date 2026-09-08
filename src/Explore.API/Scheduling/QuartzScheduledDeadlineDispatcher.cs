using Explore.Application.Contracts.Scheduling;
using Quartz;

namespace Explore.API.Scheduling;

/// <summary>
/// Attaches a single one-off trigger per deadline to a durably stored job. Retries are deliberately absent:
/// every deadline in this platform sits in front of a reconciliation sweep that owns correctness, so a lost
/// wake-up costs latency rather than work. Adding scheduler-level retry here would create a second retry
/// authority that could disagree with the sweep.
/// </summary>
public sealed class QuartzScheduledDeadlineDispatcher(
    ISchedulerFactory schedulerFactory,
    ILogger<QuartzScheduledDeadlineDispatcher> logger)
    : IScheduledDeadlineDispatcher
{
    public async Task<ScheduledDeadlineResult> ScheduleAsync(
        ScheduledDeadline deadline,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deadline);

        if (!QuartzSchedulerKeys.TryResolveDeadlineJob(deadline.JobName, out var jobKey))
        {
            // A trigger for a job the scheduler does not store would be accepted and then never run, which
            // is worse than refusing: the caller would believe the deadline is live.
            logger.LogError(
                "Deadline registration refused for unknown scheduled job {JobName}.",
                deadline.JobName);
            return ScheduledDeadlineResult.NotScheduled(ScheduledDeadlineResult.SchedulerUnavailable);
        }

        var triggerKey = QuartzSchedulerKeys.DeadlineTriggerFor(deadline.JobName, deadline.DeadlineKey);

        try
        {
            var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
            var trigger = BuildTrigger(deadline, jobKey, triggerKey);

            // Replacing through the deterministic key is what keeps a re-registration — a retried command,
            // a changed expiry — from leaving two wake-ups behind for the same aggregate.
            if (await scheduler.CheckExists(triggerKey, cancellationToken))
            {
                await scheduler.RescheduleJob(triggerKey, trigger, cancellationToken);
            }
            else
            {
                await scheduler.ScheduleJob(trigger, cancellationToken);
            }

            return ScheduledDeadlineResult.Success();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Only the exception type is logged: provider errors carry connection detail that the platform
            // keeps out of logs by convention.
            logger.LogWarning(
                "Deadline registration failed for job {JobName}. FailureType={FailureType}",
                deadline.JobName,
                exception.GetType().Name);
            return ScheduledDeadlineResult.NotScheduled(ScheduledDeadlineResult.SchedulerUnavailable);
        }
    }

    public async Task<bool> CancelAsync(string jobName, string deadlineKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        ArgumentException.ThrowIfNullOrWhiteSpace(deadlineKey);

        try
        {
            var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
            return await scheduler.UnscheduleJob(
                QuartzSchedulerKeys.DeadlineTriggerFor(jobName, deadlineKey),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A deadline that outlives its work fires once and finds nothing to do, so a failed cancel is
            // wasted wake-up rather than incorrect behaviour, and must not fail the caller's transition.
            logger.LogWarning(
                "Deadline cancellation failed for job {JobName}. FailureType={FailureType}",
                jobName,
                exception.GetType().Name);
            return false;
        }
    }

    /// <summary>
    /// Pointer entries become individual string entries rather than a serialized object, which matches the
    /// store's <c>UseProperties</c> mode and keeps scheduler rows readable to operators without a decoder.
    /// </summary>
    private static ITrigger BuildTrigger(ScheduledDeadline deadline, JobKey jobKey, TriggerKey triggerKey)
    {
        var jobData = new JobDataMap();
        foreach (var (key, value) in deadline.Pointer)
        {
            jobData[key] = value;
        }

        return TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .StartAt(deadline.DueAt)
            .UsingJobData(jobData)
            // A deadline missed during downtime is still due work, so it runs at startup rather than being
            // discarded; the owning sweep would otherwise have to wait for its own next pass.
            .WithSimpleSchedule(schedule => schedule.WithMisfireHandlingInstructionFireNow())
            .Build();
    }
}
