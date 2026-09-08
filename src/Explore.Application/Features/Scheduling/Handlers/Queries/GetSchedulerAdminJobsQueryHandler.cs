using Explore.Application.Contracts.Scheduling;
using Explore.Application.DTOs.Scheduling;
using Explore.Application.Features.Scheduling.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Scheduling.Handlers.Queries;

public sealed class GetSchedulerAdminJobsQueryHandler(
    ISchedulerOperations schedulerOperations,
    IScheduledJobRegistry jobRegistry)
    : IRequestHandler<GetSchedulerAdminJobsQuery, IReadOnlyList<SchedulerAdminJobDto>>
{
    public async Task<IReadOnlyList<SchedulerAdminJobDto>> Handle(
        GetSchedulerAdminJobsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var snapshot = await schedulerOperations.GetSnapshotAsync(cancellationToken);

        // The adapter already orders by group then name, so the operator sees a stable list across refreshes
        // rather than rows that reshuffle whenever the scheduler enumerates its store differently.
        return [.. snapshot.Jobs.Select(job => SchedulerAdminProjection.MapJob(job, jobRegistry))];
    }
}
