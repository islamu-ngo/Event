using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IActorSubscriptionRepository : IGenericRepository<ActorSubscription, Guid>
{
    /// <summary>Reads current scalar state in the exact execution tenant, never a tracked or retired subscription.</summary>
    Task<ActorSubscription?> GetBySubscriberAndTargetAsync(
        Guid tenantId,
        Guid subscriberTenantUserId,
        Guid targetActorId,
        CancellationToken cancellationToken = default);

    Task<ActorSubscription?> GetDiscoverableBySubscriberAndTargetAsync(
        Guid tenantId,
        Guid subscriberTenantUserId,
        Guid targetActorId,
        CancellationToken cancellationToken = default);

    Task<(List<ActorSubscription> Items, int TotalCount)> GetBySubscriberPagedAsync(
        Guid tenantId,
        Guid subscriberTenantUserId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<List<ActorSubscription>> GetActiveFanoutBatchAsync(
        Guid tenantId,
        Guid targetActorId,
        Guid? afterSubscriberTenantUserId,
        int pageSize,
        CancellationToken cancellationToken = default);
}
