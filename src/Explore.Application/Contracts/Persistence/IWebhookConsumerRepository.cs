using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IWebhookConsumerRepository
{
    Task<WebhookConsumer> CreateAsync(WebhookConsumer consumer, CancellationToken cancellationToken);

    Task<WebhookConsumer?> GetByIdForOwnerOperationAsync(
        Guid consumerId,
        bool forUpdate,
        CancellationToken cancellationToken);

    Task<WebhookConsumer?> GetByOwnerAndIdAsync(
        WebhookOwnershipScope ownership,
        Guid consumerId,
        bool forUpdate,
        CancellationToken cancellationToken);

    Task<WebhookConsumer?> GetByOwnerAndNameAsync(
        WebhookOwnershipScope ownership,
        string name,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WebhookConsumer>> ListByOwnerAsync(
        WebhookOwnershipScope ownership,
        int limit,
        CancellationToken cancellationToken);

    Task<WebhookConsumer?> GetByTenantAndIdAsync(
        Guid tenantId,
        Guid consumerId,
        CancellationToken cancellationToken);

    Task<WebhookConsumer?> GetByTenantAndIdForUpdateAsync(
        Guid tenantId,
        Guid consumerId,
        CancellationToken cancellationToken);

    Task<WebhookConsumer> UpdateAsync(
        WebhookConsumer consumer,
        CancellationToken cancellationToken);

    Task<WebhookConsumer?> GetByTenantAndNameAsync(
        Guid tenantId,
        string name,
        CancellationToken cancellationToken);

    Task<WebhookConsumer?> GetByExternalProviderAppIdAsync(
        Guid tenantId,
        string externalProviderAppId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WebhookConsumer>> ListByTenantAsync(
        Guid tenantId,
        int limit,
        CancellationToken cancellationToken);
}
