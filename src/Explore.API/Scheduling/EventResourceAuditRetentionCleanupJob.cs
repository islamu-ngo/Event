using Explore.Application.Contracts.Scheduling;
using Explore.Application.Services;
using Quartz;

namespace Explore.API.Scheduling;

[DisallowConcurrentExecution]
public sealed class EventResourceAuditRetentionCleanupJob(
    EventResourceAuditRetentionService retention,
    ILogger<EventResourceAuditRetentionCleanupJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        await retention.CleanupAsync(context.CancellationToken);
        logger.LogInformation("Scheduled job {JobName} completed.", ScheduledJobNames.EventResourceAuditRetentionCleanup);
    }
}
