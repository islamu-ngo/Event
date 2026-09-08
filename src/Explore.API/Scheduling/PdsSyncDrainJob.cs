using Explore.Application.Contracts.Scheduling;
using Explore.Application.Contracts.Services;
using Quartz;

namespace Explore.API.Scheduling;

[DisallowConcurrentExecution]
public sealed class PdsSyncDrainJob(
    IPdsSyncDrainService service,
    ILogger<PdsSyncDrainJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        PdsSyncDrainResult result = await service.ProcessBatchAsync(context.CancellationToken);
        logger.LogInformation(
            "Scheduled job {JobName} completed. Claimed={ClaimedCount}, Failed={FailedCount}, ClaimLost={ClaimLostCount}",
            ScheduledJobNames.PdsSyncDrain,
            result.ClaimedCount,
            result.FailedCount,
            result.ClaimLostCount);
    }
}
