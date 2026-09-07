using Explore.Application.Contracts.Scheduling;
using Quartz;
using Quartz.Impl.Matchers;

namespace Explore.API.Scheduling;

/// <summary>
/// Reads and controls the live Quartz scheduler on behalf of the instance administration surface. It is the only
/// implementation of <see cref="ISchedulerOperations"/> that talks to a scheduler library, which is what keeps
/// Quartz out of Application and Domain while still giving operators real control.
/// </summary>
public sealed class QuartzSchedulerOperations(
    ISchedulerFactory schedulerFactory,
    IScheduledJobRegistry jobRegistry) : ISchedulerOperations
{
    public async Task<SchedulerRuntimeSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        var metadata = await scheduler.GetMetaData(cancellationToken);
        var jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup(), cancellationToken);

        // Executing keys are read once up front rather than per job: the set is small, and a single read keeps the
        // whole snapshot consistent instead of letting a job start mid-enumeration and appear twice differently.
        var executingJobs = await scheduler.GetCurrentlyExecutingJobs(cancellationToken);
        var executingKeys = executingJobs
            .Select(context => context.JobDetail.Key)
            .ToHashSet();

        List<SchedulerJobSnapshot> jobs = [];
        foreach (var jobKey in jobKeys
                     .OrderBy(key => key.Group, StringComparer.Ordinal)
                     .ThenBy(key => key.Name, StringComparer.Ordinal))
        {
            var detail = await scheduler.GetJobDetail(jobKey, cancellationToken);
            var triggers = await scheduler.GetTriggersOfJob(jobKey, cancellationToken);

            List<SchedulerTriggerSnapshot> triggerSnapshots = [];
            foreach (var trigger in triggers.OrderBy(trigger => trigger.Key.Name, StringComparer.Ordinal))
            {
                var state = await scheduler.GetTriggerState(trigger.Key, cancellationToken);
                triggerSnapshots.Add(new SchedulerTriggerSnapshot(
                    trigger.Key.Name,
                    trigger.Key.Group,
                    MapTriggerState(state),
                    DescribeSchedule(trigger),
                    trigger.GetNextFireTimeUtc(),
                    trigger.GetPreviousFireTimeUtc()));
            }

            jobs.Add(new SchedulerJobSnapshot(
                jobKey.Name,
                jobKey.Group,
                jobRegistry.FindByName(jobKey.Name)?.Owner,
                detail?.Description,
                detail?.Durable ?? false,
                executingKeys.Contains(jobKey),
                triggerSnapshots));
        }

        return new SchedulerRuntimeSnapshot(
            Available: true,
            metadata.SchedulerName,
            metadata.SchedulerInstanceId,
            metadata.Started,
            metadata.InStandbyMode,
            metadata.Shutdown,
            metadata.JobStoreClustered,
            metadata.JobStoreSupportsPersistence,
            executingJobs.Count,
            jobs);
    }

    /// <summary>
    /// Standby is used rather than shutdown: it stops triggers from firing while leaving the scheduler resumable
    /// in-process. A shutdown scheduler cannot be restarted without recycling the host, which would turn a routine
    /// operator pause into an outage.
    /// </summary>
    public async Task<SchedulerOperationResult> PauseAllAsync(CancellationToken cancellationToken)
    {
        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        await scheduler.Standby(cancellationToken);
        return SchedulerOperationResult.Succeeded;
    }

    public async Task<SchedulerOperationResult> ResumeAllAsync(CancellationToken cancellationToken)
    {
        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        await scheduler.Start(cancellationToken);
        return SchedulerOperationResult.Succeeded;
    }

    public Task<SchedulerOperationResult> PauseJobAsync(string group, string name, CancellationToken cancellationToken) =>
        ExecuteOnJobAsync(group, name, (scheduler, key, token) => scheduler.PauseJob(key, token), cancellationToken);

    public Task<SchedulerOperationResult> ResumeJobAsync(string group, string name, CancellationToken cancellationToken) =>
        ExecuteOnJobAsync(group, name, (scheduler, key, token) => scheduler.ResumeJob(key, token), cancellationToken);

    public Task<SchedulerOperationResult> TriggerJobAsync(string group, string name, CancellationToken cancellationToken) =>
        ExecuteOnJobAsync(group, name, (scheduler, key, token) => scheduler.TriggerJob(key, token), cancellationToken);

    /// <summary>
    /// Clears the error state of every trigger on the job that is currently in it. Triggers are inspected first so
    /// a job with no errored trigger reports <see cref="SchedulerOperationOutcome.NotApplicable"/> instead of
    /// silently succeeding — an operator pressing "recover" on an already-healthy job should be told so.
    /// </summary>
    public Task<SchedulerOperationResult> ResetJobErrorStateAsync(
        string group,
        string name,
        CancellationToken cancellationToken) =>
        ResolveJobAsync(group, name, async (scheduler, jobKey, token) =>
        {
            var triggers = await scheduler.GetTriggersOfJob(jobKey, token);
            var reset = 0;

            foreach (var trigger in triggers)
            {
                if (await scheduler.GetTriggerState(trigger.Key, token) != TriggerState.Error)
                {
                    continue;
                }

                await scheduler.ResetTriggerFromErrorState(trigger.Key, token);
                reset++;
            }

            return reset > 0
                ? SchedulerOperationResult.Succeeded
                : SchedulerOperationResult.NotApplicable;
        }, cancellationToken);

    /// <summary>
    /// Signals cancellation to the job's executing instances. Quartz reports whether anything was actually
    /// signalled; a false result means the execution had already finished, which is reported as not applicable
    /// rather than as a completed interruption.
    /// </summary>
    public Task<SchedulerOperationResult> InterruptJobAsync(
        string group,
        string name,
        CancellationToken cancellationToken) =>
        ResolveJobAsync(group, name, async (scheduler, jobKey, token) =>
            await scheduler.Interrupt(jobKey, token)
                ? SchedulerOperationResult.Succeeded
                : SchedulerOperationResult.NotApplicable,
            cancellationToken);

    /// <summary>
    /// Runs an operation whose only failure mode is a missing job, reporting success once it completes.
    /// </summary>
    private Task<SchedulerOperationResult> ExecuteOnJobAsync(
        string group,
        string name,
        Func<IScheduler, JobKey, CancellationToken, Task> operation,
        CancellationToken cancellationToken) =>
        ResolveJobAsync(group, name, async (scheduler, jobKey, token) =>
        {
            await operation(scheduler, jobKey, token);
            return SchedulerOperationResult.Succeeded;
        }, cancellationToken);

    /// <summary>
    /// Resolves the job and confirms it exists before acting, then lets the operation decide the outcome. Quartz
    /// treats acting on an unknown key as a no-op, so without this check an operator would see a success response
    /// for a job that is not there.
    /// </summary>
    private async Task<SchedulerOperationResult> ResolveJobAsync(
        string group,
        string name,
        Func<IScheduler, JobKey, CancellationToken, Task<SchedulerOperationResult>> operation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(group) || string.IsNullOrWhiteSpace(name))
        {
            return SchedulerOperationResult.JobNotFound;
        }

        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        var jobKey = new JobKey(name, group);

        if (!await scheduler.CheckExists(jobKey, cancellationToken))
        {
            return SchedulerOperationResult.JobNotFound;
        }

        return await operation(scheduler, jobKey, cancellationToken);
    }

    private static string MapTriggerState(TriggerState state) => state switch
    {
        TriggerState.Normal => SchedulerAdminStates.Active,
        TriggerState.Paused => SchedulerAdminStates.Paused,
        TriggerState.Complete => SchedulerAdminStates.Complete,
        TriggerState.Error => SchedulerAdminStates.Error,
        TriggerState.Blocked => SchedulerAdminStates.Blocked,
        _ => SchedulerAdminStates.None
    };

    /// <summary>
    /// Describes a trigger's cadence in operator terms. Only the schedule shape is exposed; trigger data maps are
    /// deliberately never read, so pointer payloads cannot leak through the operator surface.
    /// </summary>
    private static string? DescribeSchedule(ITrigger trigger) => trigger switch
    {
        ICronTrigger cron => $"cron: {cron.CronExpressionString}",
        ISimpleTrigger { RepeatCount: 0 } => "once",
        ISimpleTrigger simple => $"every {DescribeInterval(simple.RepeatInterval)}",
        _ => null
    };

    private static string DescribeInterval(TimeSpan interval) => interval switch
    {
        { TotalDays: >= 1 } => $"{interval.TotalDays:0.##} d",
        { TotalHours: >= 1 } => $"{interval.TotalHours:0.##} h",
        { TotalMinutes: >= 1 } => $"{interval.TotalMinutes:0.##} min",
        _ => $"{interval.TotalSeconds:0.##} s"
    };
}
