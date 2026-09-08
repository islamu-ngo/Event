namespace Explore.Application.Contracts.Webhooks;

public interface IWebhookProviderPublicationDrainService
{
    Task<WebhookProviderPublicationDrainResult> ProcessBatchAsync(CancellationToken cancellationToken);

    Task<WebhookProviderReconciliationDrainResult> ProcessReconciliationBatchAsync(
        CancellationToken cancellationToken);
}

public sealed record WebhookProviderPublicationDrainResult(
    int ClaimedCount,
    int ProviderQueuedCount,
    int RetryScheduledCount,
    int PublicationUnknownCount,
    int DeadLetteredCount,
    int LeaseLostCount,
    int FailedCount);

public sealed record WebhookProviderReconciliationDrainResult(
    int ManualCandidateCount,
    int ClaimedCount,
    int ProviderQueuedCount,
    int RetryScheduledCount,
    int DeferredCount,
    int ManualReconciliationCount,
    int LeaseLostCount,
    int FailedCount);
