using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Extensions;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class ActorSubscriptionRepository : GenericRepository<ActorSubscription, Guid>, IActorSubscriptionRepository
{
    private readonly ExploreDbContext _dbContext;
    private readonly PostgresNativeQueueEnrollment? _nativeEnrollment;

    public ActorSubscriptionRepository(ExploreDbContext dbContext, PostgresNativeQueueEnrollment? nativeEnrollment = null) : base(dbContext)
    {
        _dbContext = dbContext;
        _nativeEnrollment = nativeEnrollment;
    }

    public async Task<ActorSubscription?> GetBySubscriberAndTargetAsync(
        Guid tenantId,
        Guid subscriberTenantUserId,
        Guid targetActorId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrentTenant(tenantId))
            return null;

        return await PersonalRows()
            .Where(subscription => subscription.TenantId == tenantId
                && subscription.SubscriberTenantUserId == subscriberTenantUserId
                && subscription.TargetActorId == targetActorId
                && subscription.SubscriberTenantUser.StatusId == (int)TenantUserStatusEnum.Active)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<ActorSubscription?> GetDiscoverableBySubscriberAndTargetAsync(
        Guid tenantId,
        Guid subscriberTenantUserId,
        Guid targetActorId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrentTenant(tenantId))
            return Task.FromResult<ActorSubscription?>(null);
        return WhereTargetIsLocallyDiscoverable(SubscriptionDetailsQuery(), tenantId)
            .FirstOrDefaultAsync(subscription =>
                subscription.TenantId == tenantId
                && subscription.SubscriberTenantUserId == subscriberTenantUserId
                && subscription.TargetActorId == targetActorId,
                cancellationToken);
    }

    public async Task<(List<ActorSubscription> Items, int TotalCount)> GetBySubscriberPagedAsync(
        Guid tenantId,
        Guid subscriberTenantUserId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrentTenant(tenantId))
            return ([], 0);
        var query = WhereTargetIsLocallyDiscoverable(SubscriptionDetailsQuery(), tenantId)
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
        if (PostgresPublicationFanoutExecutionContract.Applies(_dbContext))
            return await PostgresPublicationFanoutExecutionContract.ListAsync<ActorSubscription>(
                _dbContext, _nativeEnrollment, "subscribers",
                new { TenantId = tenantId, SourceActorId = targetActorId, After = afterSubscriberTenantUserId, Take = pageSize },
                cancellationToken);
        if (!IsCurrentTenant(tenantId)) return [];
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
                && !subscription.SubscriberTenantUser.IsDeleted
                && !subscription.SubscriberTenantUser.User.IsDeleted
                && subscription.SubscriberUserId == subscription.SubscriberTenantUser.UserId);

        if (afterSubscriberTenantUserId.HasValue)
        {
            query = query.Where(subscription => subscription.SubscriberTenantUserId.CompareTo(afterSubscriberTenantUserId.Value) > 0);
        }

        return await query
            .OrderBy(subscription => subscription.SubscriberTenantUserId)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    private bool IsCurrentTenant(Guid tenantId) =>
        tenantId != Guid.Empty && !_dbContext.IsTenantFilterBypassed && _dbContext.TenantFilterTenantId == tenantId;

    private IQueryable<ActorSubscription> PersonalRows() =>
        _dbContext.ActorSubscriptions.AsNoTracking()
            .Where(subscription => subscription.SubscriberUserId == subscription.SubscriberTenantUser.UserId
                && !subscription.SubscriberTenantUser.IsDeleted
                && !subscription.SubscriberTenantUser.User.IsDeleted);

    private IQueryable<ActorSubscription> SubscriptionDetailsQuery()
    {
        return PersonalRows()
            .Include(subscription => subscription.TargetActor)
                .ThenInclude(actor => actor.Pii)
            .Include(subscription => subscription.TargetActorType)
            .Include(subscription => subscription.Status)
            .Include(subscription => subscription.NotificationLevel);
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
