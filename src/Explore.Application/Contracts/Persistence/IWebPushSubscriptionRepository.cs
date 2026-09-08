using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IWebPushSubscriptionRepository : IGenericRepository<WebPushSubscription, Guid>
{
    Task<WebPushSubscription> UpsertAsync(
        Guid tenantId,
        Guid userId,
        string deviceIdentifier,
        string endpoint,
        string p256Dh,
        string authSecret,
        DateTime? expirationTime,
        DateTime now,
        CancellationToken cancellationToken = default);

    Task<WebPushSubscription?> GetActiveForDeviceAsync(
        Guid tenantId,
        Guid userId,
        string deviceIdentifier,
        CancellationToken cancellationToken = default);

    Task<WebPushSubscription?> GetActiveByEndpointAsync(
        string endpoint,
        CancellationToken cancellationToken = default);

    Task<WebPushSubscription?> GetActiveByIdAsync(
        Guid tenantId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WebPushSubscription>> ListActiveForUserAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<bool> UnsubscribeAsync(
        Guid tenantId,
        Guid userId,
        Guid subscriptionId,
        DateTime now,
        CancellationToken cancellationToken = default);

    Task<bool> UnsubscribeDeviceAsync(
        Guid tenantId,
        Guid userId,
        string deviceIdentifier,
        DateTime now,
        CancellationToken cancellationToken = default);
}
