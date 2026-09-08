using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IWebhookConsumerProviderBindingRepository
{
    Task<WebhookConsumerProviderBinding> CreateAsync(
        WebhookConsumerProviderBinding binding,
        CancellationToken cancellationToken);

    Task<WebhookConsumerProviderBinding?> GetByConsumerAsync(
        Guid? tenantId,
        Guid webhookConsumerId,
        WebhookProviderKind providerKind,
        string providerEnvironment,
        CancellationToken cancellationToken);

    Task<WebhookConsumerProviderBinding?> GetByTenantAndIdForUpdateAsync(
        Guid? tenantId,
        Guid bindingId,
        CancellationToken cancellationToken);

    Task<WebhookConsumerProviderBinding?> GetVerifiedByConsumerAsync(
        Guid? tenantId,
        Guid webhookConsumerId,
        WebhookProviderKind providerKind,
        string providerEnvironment,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WebhookConsumerProviderBinding>> GetVerifiedByConsumersAsync(
        Guid? tenantId,
        IReadOnlyCollection<Guid> webhookConsumerIds,
        WebhookProviderKind providerKind,
        string providerEnvironment,
        CancellationToken cancellationToken);

    Task<WebhookConsumerProviderBinding?> GetVerifiedByProviderIdentityAsync(
        Guid tenantId,
        WebhookProviderKind providerKind,
        string providerEnvironment,
        string externalApplicationId,
        string applicationUid,
        CancellationToken cancellationToken);

    Task<WebhookConsumerProviderBinding?> ResolveVerifiedProviderIdentityAsync(
        WebhookProviderKind providerKind,
        string providerEnvironment,
        string externalApplicationId,
        string applicationUid,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
