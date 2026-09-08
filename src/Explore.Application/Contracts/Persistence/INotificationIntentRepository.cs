using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface INotificationIntentRepository : IGenericRepository<NotificationIntent, Guid>
{
    Task<NotificationIntent> CreateIntentAsync(NotificationIntent intent, CancellationToken cancellationToken = default);

    Task<NotificationIntent?> GetByTenantAndIdAsync(Guid tenantId, Guid intentId, CancellationToken cancellationToken = default);

    Task<bool> ExistsByDeduplicationKeyAsync(Guid tenantId, string deduplicationKey, CancellationToken cancellationToken = default);

    Task<NotificationDelivery> AddDeliveryAsync(NotificationDelivery delivery, CancellationToken cancellationToken = default);

    Task<NotificationExternalDelegation> AddExternalDelegationAsync(
        NotificationExternalDelegation delegation,
        CancellationToken cancellationToken = default);
}
