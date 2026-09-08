namespace Explore.Application.Contracts.Webhooks;

public sealed record WebhookBulkReplayLimits(
    int MaximumItemsPerOperation,
    int MaximumReservedItemsPerTenant,
    int MaximumFilterWindowDays,
    int OperationsPerPass,
    string PolicyVersion);

public sealed record WebhookBulkReplayProcessResult(
    int CompletedOperations,
    int ScheduledTargets,
    int FailedOperations);

public interface IWebhookBulkReplayPolicyResolver
{
    WebhookBulkReplayLimits Resolve();
}

public interface IWebhookBulkReplayService
{
    Task<WebhookBulkReplayProcessResult> ProcessQueuedAsync(
        CancellationToken cancellationToken = default);
}
