using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public sealed record WebhookEndpointFailureState(
    int ConsecutiveFailureCount,
    bool IsAutoPaused,
    bool TransitionedToAutoPaused = false);

public interface IWebhookEndpointRepository
{
    Task<WebhookEndpoint> CreateWithSubscriptionsAsync(
        WebhookEndpoint endpoint,
        IReadOnlyCollection<WebhookEndpointSubscription> subscriptions,
        CancellationToken cancellationToken);

    Task<WebhookEndpoint?> GetByIdForOwnerOperationAsync(
        Guid endpointId,
        bool forUpdate,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WebhookEndpoint>> ListByOwnerAsync(
        WebhookOwnershipScope ownership,
        Guid? consumerId,
        int limit,
        CancellationToken cancellationToken);

    Task<WebhookEndpoint?> GetByConsumerAndUrlForOwnerOperationAsync(
        Guid consumerId,
        string url,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WebhookEndpoint>> ListByTenantAsync(
        Guid tenantId,
        Guid? consumerId,
        int limit,
        CancellationToken cancellationToken);

    Task<WebhookEndpoint?> GetByTenantAndIdAsync(
        Guid tenantId,
        Guid endpointId,
        CancellationToken cancellationToken);

    Task<WebhookEndpoint?> GetByTenantAndIdForUpdateAsync(
        Guid tenantId,
        Guid endpointId,
        CancellationToken cancellationToken);

    Task<WebhookEndpoint?> GetByTenantConsumerAndUrlAsync(
        Guid tenantId,
        Guid consumerId,
        string url,
        CancellationToken cancellationToken);

    Task<WebhookEndpoint> UpdateWithSubscriptionsAsync(
        WebhookEndpoint endpoint,
        IReadOnlyCollection<WebhookEndpointSubscription> subscriptions,
        CancellationToken cancellationToken);

    Task<WebhookEndpoint> UpdateAsync(
        WebhookEndpoint endpoint,
        CancellationToken cancellationToken);

    Task ArchiveAsync(
        Guid? tenantId,
        Guid endpointId,
        DateTime archivedAt,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WebhookEndpoint>> GetActiveSubscribedEndpointsAsync(
        Guid tenantId,
        string eventTypeName,
        WebhookProviderMode providerMode,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WebhookEndpoint>> GetActiveSubscribedEndpointsByConsumerAsync(
        Guid? tenantId,
        Guid consumerId,
        string eventTypeName,
        CancellationToken cancellationToken);

    Task<bool> HasActiveSubscribedEndpointByConsumerAsync(
        Guid? tenantId,
        Guid consumerId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WebhookLocalTargetSnapshot>> GetEligiblePendingTargetsForUpdateAsync(
        Guid? tenantId,
        Guid endpointId,
        CancellationToken cancellationToken);

    Task<bool> TryPauseAsync(
        Guid? tenantId,
        Guid endpointId,
        long expectedDeliveryStateVersion,
        DateTime pausedAt,
        Guid actorUserId,
        CancellationToken cancellationToken);

    Task MarkSuccessAsync(
        Guid tenantId,
        Guid endpointId,
        DateTime succeededAt,
        CancellationToken cancellationToken);

    Task<WebhookEndpointFailureState> RecordFailureAsync(
        Guid tenantId,
        Guid endpointId,
        Guid localTargetId,
        Guid leaseToken,
        long deliveryFence,
        DateTime failedAt,
        string failureCategory,
        int autoPauseThreshold,
        CancellationToken cancellationToken);

    Task<bool> TryResumeAsync(
        Guid? tenantId,
        Guid endpointId,
        long expectedDeliveryStateVersion,
        DateTime resumedAt,
        Guid actorUserId,
        CancellationToken cancellationToken);
}
