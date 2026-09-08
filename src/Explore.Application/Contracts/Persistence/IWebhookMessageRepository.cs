using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IWebhookMessageRepository
{
    Task<WebhookMessage> CreateAsync(WebhookMessage message, CancellationToken cancellationToken);

    Task<WebhookMessage?> GetByIdForOwnerOperationAsync(
        Guid messageId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WebhookMessage>> ListByOwnerAsync(
        WebhookOwnershipScope ownership,
        int limit,
        CancellationToken cancellationToken);

    Task<WebhookMessage?> GetByTenantAndIdAsync(
        Guid tenantId,
        Guid messageId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WebhookMessage>> ListByTenantAsync(
        Guid tenantId,
        int limit,
        CancellationToken cancellationToken);

    Task<int> ClearExpiredPayloadsAsync(
        DateTime now,
        int batchSize,
        CancellationToken cancellationToken);
}
