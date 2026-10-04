using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Scheduling;
using Explore.Application.Features.Events.Discovery.Commands;
using Quartz;

namespace Explore.API.Scheduling;

[DisallowConcurrentExecution]
[PersistJobDataAfterExecution]
public sealed class EventDiscoverySnapshotPurgeJob(
    ICommandHandler<PurgeEventDiscoverySnapshotsCommand, PurgeEventDiscoverySnapshotsResult> handler,
    ILogger<EventDiscoverySnapshotPurgeJob> logger) : IJob
{
    private const string TenantCursorKey = "afterTenantId";

    public async Task Execute(IJobExecutionContext context)
    {
        string? cursor = context.JobDetail.JobDataMap.GetString(TenantCursorKey);
        Guid? after = null;
        if (!string.IsNullOrEmpty(cursor))
        {
            if (!Guid.TryParse(cursor, out var parsed) || parsed == Guid.Empty)
            {
                context.JobDetail.JobDataMap[TenantCursorKey] = string.Empty;
                logger.LogWarning("Scheduled job {JobName} dropped a malformed maintenance cursor.",
                    ScheduledJobNames.EventDiscoverySnapshotPurge);
                return;
            }
            after = parsed;
        }
        var result = await handler.ExecuteAsync(new(after), context.CancellationToken);
        context.JobDetail.JobDataMap[TenantCursorKey] = result.NextTenantId?.ToString("D") ?? string.Empty;
        logger.LogInformation("Scheduled job {JobName} completed.", ScheduledJobNames.EventDiscoverySnapshotPurge);
    }
}
