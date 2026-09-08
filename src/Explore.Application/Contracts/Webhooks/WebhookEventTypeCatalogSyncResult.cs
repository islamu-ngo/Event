namespace Explore.Application.Contracts.Webhooks;

public sealed record WebhookEventTypeCatalogSyncResult(
    int CreatedCount,
    int UpdatedCount,
    int UnchangedCount);
