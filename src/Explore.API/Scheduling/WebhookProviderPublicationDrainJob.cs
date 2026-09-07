using Explore.Application.Contracts.Scheduling;
using Explore.Application.Contracts.Webhooks;
using Quartz;

namespace Explore.API.Scheduling;

[DisallowConcurrentExecution]
public sealed class WebhookProviderPublicationDrainJob(
    IWebhookProviderPublicationDrainService service,
    ILogger<WebhookProviderPublicationDrainJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        WebhookProviderPublicationDrainResult dispatch = await service.ProcessBatchAsync(context.CancellationToken);
        WebhookProviderReconciliationDrainResult reconciliation =
            await service.ProcessReconciliationBatchAsync(context.CancellationToken);
        logger.LogInformation(
            "Scheduled job {JobName} completed. Claimed={ClaimedCount}, Unknown={UnknownCount}, Reconciled={ReconciledCount}",
            ScheduledJobNames.WebhookProviderPublicationDrain,
            dispatch.ClaimedCount,
            dispatch.PublicationUnknownCount,
            reconciliation.ProviderQueuedCount);
    }
}
