using Explore.Application.Contracts.Scheduling;
using Quartz;
using Quartz.Impl.Matchers;

namespace Explore.API.Scheduling;

public static class QuartzSchedulerStatusEndpoint
{
    public static async Task<IResult> HandleAsync(
        ISchedulerFactory schedulerFactory,
        IScheduledJobRegistry jobRegistry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schedulerFactory);
        ArgumentNullException.ThrowIfNull(jobRegistry);

        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        var metadata = await scheduler.GetMetaData(cancellationToken);
        var jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup(), cancellationToken);

        List<QuartzSchedulerJobStatus> jobs = [];
        foreach (var jobKey in jobKeys.OrderBy(key => key.Group, StringComparer.Ordinal)
                     .ThenBy(key => key.Name, StringComparer.Ordinal))
        {
            var triggers = await scheduler.GetTriggersOfJob(jobKey, cancellationToken);
            List<QuartzSchedulerTriggerStatus> triggerStates = [];

            foreach (var trigger in triggers)
            {
                var state = await scheduler.GetTriggerState(trigger.Key, cancellationToken);
                triggerStates.Add(new QuartzSchedulerTriggerStatus(
                    trigger.Key.Name,
                    trigger.Key.Group,
                    state.ToString(),
                    trigger.GetNextFireTimeUtc(),
                    trigger.GetPreviousFireTimeUtc()));
            }

            jobs.Add(new QuartzSchedulerJobStatus(
                jobKey.Name,
                jobKey.Group,
                jobRegistry.FindByName(jobKey.Name)?.Owner,
                triggerStates));
        }

        return Results.Ok(new QuartzSchedulerStatus(
            metadata.SchedulerName,
            metadata.SchedulerInstanceId,
            metadata.Started,
            metadata.InStandbyMode,
            metadata.Shutdown,
            metadata.JobStoreClustered,
            metadata.JobStoreSupportsPersistence,
            jobs,
            jobRegistry.ListJobs()
                .Where(descriptor => descriptor.Status == ScheduledJobOperationalStatus.Planned)
                .Select(descriptor => descriptor.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray()));
    }
}

public sealed record QuartzSchedulerStatus(
    string SchedulerName,
    string SchedulerInstanceId,
    bool Started,
    bool InStandbyMode,
    bool Shutdown,
    bool Clustered,
    bool SupportsPersistence,
    IReadOnlyCollection<QuartzSchedulerJobStatus> Jobs,
    IReadOnlyCollection<string> PlannedJobs);

public sealed record QuartzSchedulerJobStatus(
    string Name,
    string Group,
    string? Owner,
    IReadOnlyCollection<QuartzSchedulerTriggerStatus> Triggers);

public sealed record QuartzSchedulerTriggerStatus(
    string Name,
    string Group,
    string State,
    DateTimeOffset? NextFireTimeUtc,
    DateTimeOffset? PreviousFireTimeUtc);
