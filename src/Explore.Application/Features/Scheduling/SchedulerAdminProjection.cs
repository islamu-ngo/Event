using Explore.Application.Contracts.Scheduling;
using Explore.Application.DTOs.Scheduling;

namespace Explore.Application.Features.Scheduling;

/// <summary>
/// Translation rules shared by the scheduler administration queries. Centralising them means the summary counts
/// on the overview and the per-row states in the job collection are derived the same way, so the two resources
/// cannot disagree about whether a job is paused.
/// </summary>
internal static class SchedulerAdminProjection
{
    public static string ResolveSchedulerState(SchedulerRuntimeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.Available)
        {
            return SchedulerAdminStates.Disabled;
        }

        if (snapshot.Shutdown)
        {
            return SchedulerAdminStates.Shutdown;
        }

        return snapshot.InStandbyMode || !snapshot.Started
            ? SchedulerAdminStates.Standby
            : SchedulerAdminStates.Running;
    }

    public static SchedulerAdminJobDto MapJob(SchedulerJobSnapshot job, IScheduledJobRegistry jobRegistry)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(jobRegistry);

        var triggers = job.Triggers
            .Select(trigger => new SchedulerAdminTriggerDto
            {
                Name = trigger.Name,
                Group = trigger.Group,
                State = trigger.State,
                ScheduleSummary = trigger.ScheduleSummary,
                NextFireTimeUtc = trigger.NextFireTimeUtc,
                PreviousFireTimeUtc = trigger.PreviousFireTimeUtc
            })
            .ToArray();

        return new SchedulerAdminJobDto
        {
            Name = job.Name,
            Group = job.Group,
            Owner = job.Owner ?? jobRegistry.FindByName(job.Name)?.Owner,
            Description = job.Description,
            Durable = job.Durable,
            Executing = job.Executing,
            State = ResolveJobState(triggers),

            // The job-level timeline is the soonest upcoming and the most recent past fire across all triggers,
            // so one table row answers "when next / when last" without expanding the trigger list.
            NextFireTimeUtc = triggers
                .Select(trigger => trigger.NextFireTimeUtc)
                .Where(next => next.HasValue)
                .Min(),
            PreviousFireTimeUtc = triggers
                .Select(trigger => trigger.PreviousFireTimeUtc)
                .Where(previous => previous.HasValue)
                .Max(),
            Triggers = triggers
        };
    }

    /// <summary>
    /// Collapses trigger states into one job state. A durable job with no trigger is <c>on-demand</c> rather than
    /// idle, because runtime code attaches one-off triggers to it; that distinction matters to an operator
    /// deciding whether a missing trigger is a fault.
    /// </summary>
    public static string ResolveJobState(IReadOnlyCollection<SchedulerAdminTriggerDto> triggers)
    {
        ArgumentNullException.ThrowIfNull(triggers);

        if (triggers.Count == 0)
        {
            return SchedulerAdminStates.OnDemand;
        }

        if (triggers.Any(trigger => string.Equals(trigger.State, SchedulerAdminStates.Error, StringComparison.Ordinal)))
        {
            return SchedulerAdminStates.Error;
        }

        if (triggers.Any(trigger => string.Equals(trigger.State, SchedulerAdminStates.Blocked, StringComparison.Ordinal)))
        {
            return SchedulerAdminStates.Blocked;
        }

        return triggers.All(trigger => string.Equals(trigger.State, SchedulerAdminStates.Paused, StringComparison.Ordinal))
            ? SchedulerAdminStates.Paused
            : SchedulerAdminStates.Active;
    }

    public static IReadOnlyList<string> ListPlannedJobNames(IScheduledJobRegistry jobRegistry)
    {
        ArgumentNullException.ThrowIfNull(jobRegistry);

        return
        [
            .. jobRegistry.ListJobs()
                .Where(descriptor => descriptor.Status == ScheduledJobOperationalStatus.Planned)
                .Select(descriptor => descriptor.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
        ];
    }
}
