using Explore.Application.Contracts.Scheduling;
using Explore.Application.Contracts.Webhooks;
using Quartz;

namespace Explore.API.Scheduling;

[DisallowConcurrentExecution]
public sealed class IncomingWebhookIntakeDrainJob(
    IIncomingWebhookDrainService service,
    ILogger<IncomingWebhookIntakeDrainJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        IncomingWebhookDrainResult result = await service.ProcessBatchAsync(context.CancellationToken);
        logger.LogInformation(
            "Scheduled job {JobName} completed. Claimed={ClaimedCount}, Failed={FailedCount}",
            ScheduledJobNames.IncomingWebhookIntakeDrain,
            result.ClaimedCount,
            result.FailedCount);
    }
}
