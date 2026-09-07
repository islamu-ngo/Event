using Explore.Application.Contracts.Scheduling;
using Explore.Application.Contracts.Services;
using Quartz;

namespace Explore.API.Scheduling;

[DisallowConcurrentExecution]
public sealed class IntegrationSyncDrainJob(
    IIntegrationSyncDrainService service,
    ILogger<IntegrationSyncDrainJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        IntegrationSyncDrainResult result = await service.ProcessBatchAsync(context.CancellationToken);
        logger.LogInformation(
            "Scheduled job {JobName} completed. Processed={ProcessedCount}, Ambiguous={AmbiguousCount}",
            ScheduledJobNames.IntegrationSyncDrain,
            result.Processed,
            result.Ambiguous);
    }
}
