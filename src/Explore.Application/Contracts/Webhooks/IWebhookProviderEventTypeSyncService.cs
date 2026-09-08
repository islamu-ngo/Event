namespace Explore.Application.Contracts.Webhooks;

public interface IWebhookProviderEventTypeSyncService
{
    Task<WebhookProviderEventTypeSyncResult> SyncAsync(CancellationToken cancellationToken);
}

public sealed record WebhookProviderEventTypeSyncResult(
    bool Succeeded,
    int SyncedCount,
    IReadOnlyCollection<WebhookProviderEventTypeSyncFailure> Failures)
{
    public static WebhookProviderEventTypeSyncResult Success(int syncedCount) =>
        new(true, syncedCount, []);

    public static WebhookProviderEventTypeSyncResult Completed(
        int syncedCount,
        IReadOnlyCollection<WebhookProviderEventTypeSyncFailure> failures) =>
        new(failures.Count == 0, syncedCount, failures);
}

public sealed record WebhookProviderEventTypeSyncFailure(
    string EventType,
    string FailureCategory,
    bool IsRetryable,
    string SafeDetail);
