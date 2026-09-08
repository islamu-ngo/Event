using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Extensions;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class ActorSubscriptionRepository : GenericRepository<ActorSubscription, Guid>, IActorSubscriptionRepository
{
    private readonly ExploreDbContext _dbContext;

    public ActorSubscriptionRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ActorSubscription?> GetBySubscriberAndTargetAsync(
        Guid tenantId,
        Guid subscriberTenantUserId,
        Guid targetActorId,
        bool trackChanges = false,
        CancellationToken cancellationToken = default)
    {
        var query = SubscriptionDetailsQuery(trackChanges)
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate);

        return await query.FirstOrDefaultAsync(subscription =>
            subscription.TenantId == tenantId
            && subscription.SubscriberTenantUserId == subscriberTenantUserId
            && subscription.TargetActorId == targetActorId,
            cancellationToken);
    }

    public Task<ActorSubscription?> GetDiscoverableBySubscriberAndTargetAsync(
        Guid tenantId,
        Guid subscriberTenantUserId,
        Guid targetActorId,
        CancellationToken cancellationToken = default) =>
        WhereTargetIsLocallyDiscoverable(
                SubscriptionDetailsQuery(trackChanges: false)
                    .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate),
                tenantId)
            .FirstOrDefaultAsync(subscription =>
                subscription.TenantId == tenantId
                && subscription.SubscriberTenantUserId == subscriberTenantUserId
                && subscription.TargetActorId == targetActorId,
                cancellationToken);

    public async Task<(List<ActorSubscription> Items, int TotalCount)> GetBySubscriberPagedAsync(
        Guid tenantId,
        Guid subscriberTenantUserId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = WhereTargetIsLocallyDiscoverable(
                SubscriptionDetailsQuery(trackChanges: false)
                    .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate),
                tenantId)
            .Where(subscription => subscription.TenantId == tenantId
                && subscription.SubscriberTenantUserId == subscriberTenantUserId)
            .OrderByDescending(subscription => subscription.SubscribedAt);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<List<ActorSubscription>> GetActiveFanoutBatchAsync(
        Guid tenantId,
        Guid targetActorId,
        Guid? afterSubscriberTenantUserId,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        IQueryable<ActorSubscription> query = WhereTargetIsLocallyDiscoverable(
                _dbContext.ActorSubscriptions
                    .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                    .AsNoTracking()
                    .Include(subscription => subscription.SubscriberTenantUser),
                tenantId)
            .Where(subscription => subscription.TenantId == tenantId
                && subscription.TargetActorId == targetActorId
                && subscription.StatusId == (int)ActorSubscriptionStatusEnum.Active
                && subscription.NotificationLevelId == (int)ActorSubscriptionNotificationLevelEnum.All
                && subscription.SubscriberTenantUser.StatusId == (int)TenantUserStatusEnum.Active
                && !subscription.SubscriberTenantUser.IsDeleted);

        if (afterSubscriberTenantUserId.HasValue)
        {
            query = query.Where(subscription => subscription.SubscriberTenantUserId.CompareTo(afterSubscriberTenantUserId.Value) > 0);
        }

        return await query
            .OrderBy(subscription => subscription.SubscriberTenantUserId)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    private IQueryable<ActorSubscription> SubscriptionDetailsQuery(bool trackChanges)
    {
        var query = _dbContext.ActorSubscriptions
            .Include(subscription => subscription.TargetActor)
                .ThenInclude(actor => actor.Pii)
            .Include(subscription => subscription.TargetActorType)
            .Include(subscription => subscription.Status)
            .Include(subscription => subscription.NotificationLevel);

        return trackChanges ? query : query.AsNoTracking();
    }

    private IQueryable<ActorSubscription> WhereTargetIsLocallyDiscoverable(
        IQueryable<ActorSubscription> query,
        Guid tenantId)
    {
        IQueryable<Guid> actorIds = _dbContext.Actors
            .WhereLocallyDiscoverable(_dbContext, tenantId)
            .Select(actor => actor.Id);

        return query.Where(subscription => actorIds.Contains(subscription.TargetActorId));
    }
}
