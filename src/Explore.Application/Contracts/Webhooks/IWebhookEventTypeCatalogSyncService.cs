namespace Explore.Application.Contracts.Webhooks;

public interface IWebhookEventTypeCatalogSyncService
{
    Task<WebhookEventTypeCatalogSyncResult> SyncAsync(CancellationToken cancellationToken);
}
