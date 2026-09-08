using Explore.Application.Contracts.Scheduling;
using Explore.Application.DTOs.Scheduling;
using Explore.Application.Features.Scheduling.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Scheduling.Handlers.Queries;

public sealed class GetSchedulerAdminOverviewQueryHandler(
    ISchedulerOperations schedulerOperations,
    IScheduledJobRegistry jobRegistry,
    ISchedulerAdminPolicy policy)
    : IRequestHandler<GetSchedulerAdminOverviewQuery, SchedulerAdminOverviewDto>
{
    public async Task<SchedulerAdminOverviewDto> Handle(
        GetSchedulerAdminOverviewQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var snapshot = await schedulerOperations.GetSnapshotAsync(cancellationToken);
        var jobs = snapshot.Jobs
            .Select(job => SchedulerAdminProjection.MapJob(job, jobRegistry))
            .ToArray();

        return new SchedulerAdminOverviewDto
        {
            GeneratedAtUtc = DateTime.UtcNow,
            Available = snapshot.Available,
            ReadOnly = policy.IsReadOnly,
            SchedulerName = snapshot.SchedulerName,
            InstanceId = snapshot.InstanceId,
            State = SchedulerAdminProjection.ResolveSchedulerState(snapshot),
            Clustered = snapshot.Clustered,

            // Executing-job reads and interruption are answered per node, not across a cluster. In a clustered
            // deployment the caller is therefore seeing one node's view, and the surface says so rather than
            // letting an operator read "not running" as "not running anywhere".
            ExecutingViewIsNodeLocal = snapshot.Clustered,
            SupportsPersistence = snapshot.SupportsPersistence,
            ExecutingJobCount = snapshot.ExecutingJobCount,
            JobCount = jobs.Length,
            PausedJobCount = jobs.Count(job =>
                string.Equals(job.State, SchedulerAdminStates.Paused, StringComparison.Ordinal)),
            ErroredJobCount = jobs.Count(job =>
                string.Equals(job.State, SchedulerAdminStates.Error, StringComparison.Ordinal)),
            PlannedJobs = SchedulerAdminProjection.ListPlannedJobNames(jobRegistry)
        };
    }
}
