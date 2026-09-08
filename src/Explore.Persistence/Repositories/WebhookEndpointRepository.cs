using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class WebhookEndpointRepository : IWebhookEndpointRepository
{
    private readonly ExploreDbContext _dbContext;

    public WebhookEndpointRepository(ExploreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<WebhookEndpoint> CreateWithSubscriptionsAsync(
        WebhookEndpoint endpoint,
        IReadOnlyCollection<WebhookEndpointSubscription> subscriptions,
        CancellationToken cancellationToken)
    {
        if (endpoint.Id == Guid.Empty)
        {
            endpoint.Id = Guid.CreateVersion7();
        }

        foreach (var subscription in subscriptions)
        {
            if (subscription.Id == Guid.Empty)
            {
                subscription.Id = Guid.CreateVersion7();
            }

            subscription.TenantId = endpoint.TenantId;
            subscription.InstanceId = endpoint.InstanceId;
            subscription.EndpointId = endpoint.Id;
        }

        await _dbContext.WebhookEndpoints.AddAsync(endpoint, cancellationToken);
        if (subscriptions.Count > 0)
        {
            await _dbContext.WebhookEndpointSubscriptions.AddRangeAsync(subscriptions, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return endpoint;
    }

    public async Task<WebhookEndpoint?> GetByIdForOwnerOperationAsync(
        Guid endpointId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        IQueryable<WebhookEndpoint> query = _dbContext.WebhookEndpoints
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookOwnerOperation)
            .Include(endpoint => endpoint.Consumer)
            .ThenInclude(consumer => consumer!.ProviderBindings)
            .Include(endpoint => endpoint.Subscriptions)
            .ThenInclude(subscription => subscription.EventType);

        if (!forUpdate)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(endpoint => endpoint.Id == endpointId, cancellationToken);
    }

    public async Task<IReadOnlyList<WebhookEndpoint>> ListByOwnerAsync(
        WebhookOwnershipScope ownership,
        Guid? consumerId,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = ApplyOwnerPredicate(
                _dbContext.WebhookEndpoints
                    .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookOwnerOperation),
                ownership)
            .AsNoTracking()
            .Include(endpoint => endpoint.Consumer)
            .Include(endpoint => endpoint.Subscriptions)
            .ThenInclude(subscription => subscription.EventType)
            .Where(endpoint => endpoint.StatusId != (int)WebhookEndpointStatus.Archived);

        if (consumerId.HasValue)
        {
            query = query.Where(endpoint => endpoint.ConsumerId == consumerId.Value);
        }

        return await query
            .OrderBy(endpoint => endpoint.Consumer != null ? endpoint.Consumer.Name : string.Empty)
            .ThenBy(endpoint => endpoint.CreatedAt)
            .ThenBy(endpoint => endpoint.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<WebhookEndpoint?> GetByConsumerAndUrlForOwnerOperationAsync(
        Guid consumerId,
        string url,
        CancellationToken cancellationToken)
    {
        return await _dbContext.WebhookEndpoints
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookOwnerOperation)
            .AsNoTracking()
            .FirstOrDefaultAsync(endpoint =>
                endpoint.ConsumerId == consumerId &&
                endpoint.Url == url &&
                endpoint.StatusId != (int)WebhookEndpointStatus.Archived,
                cancellationToken);
    }

    public async Task<IReadOnlyList<WebhookEndpoint>> ListByTenantAsync(
        Guid tenantId,
        Guid? consumerId,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.WebhookEndpoints
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .Include(e => e.Consumer)
            .Include(e => e.Subscriptions)
            .ThenInclude(e => e.EventType)
            .Where(e => e.TenantId == tenantId && e.StatusId != (int)WebhookEndpointStatus.Archived);

        if (consumerId is not null)
        {
            query = query.Where(e => e.ConsumerId == consumerId.Value);
        }

        return await query
            .OrderBy(e => e.Consumer != null ? e.Consumer.Name : string.Empty)
            .ThenBy(e => e.CreatedAt)
            .ThenBy(e => e.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<WebhookEndpoint?> GetByTenantAndIdAsync(
        Guid tenantId,
        Guid endpointId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.WebhookEndpoints
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .Include(e => e.Consumer)
            .Include(e => e.Subscriptions)
            .ThenInclude(e => e.EventType)
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == endpointId, cancellationToken);
    }

    public async Task<WebhookEndpoint?> GetByTenantAndIdForUpdateAsync(
        Guid tenantId,
        Guid endpointId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.WebhookEndpoints
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == endpointId, cancellationToken);
    }

    public async Task<WebhookEndpoint?> GetByTenantConsumerAndUrlAsync(
        Guid tenantId,
        Guid consumerId,
        string url,
        CancellationToken cancellationToken)
    {
        return await _dbContext.WebhookEndpoints
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId
                && e.ConsumerId == consumerId
                && e.Url == url
                && e.StatusId != (int)WebhookEndpointStatus.Archived, cancellationToken);
    }

    public async Task<WebhookEndpoint> UpdateWithSubscriptionsAsync(
        WebhookEndpoint endpoint,
        IReadOnlyCollection<WebhookEndpointSubscription> subscriptions,
        CancellationToken cancellationToken)
    {
        var existingSubscriptions = await _dbContext.WebhookEndpointSubscriptions
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .Where(subscription => subscription.TenantId == endpoint.TenantId && subscription.EndpointId == endpoint.Id)
            .ToListAsync(cancellationToken);

        _dbContext.WebhookEndpointSubscriptions.RemoveRange(existingSubscriptions);

        foreach (var subscription in subscriptions)
        {
            if (subscription.Id == Guid.Empty)
            {
                subscription.Id = Guid.CreateVersion7();
            }

            subscription.TenantId = endpoint.TenantId;
            subscription.InstanceId = endpoint.InstanceId;
            subscription.EndpointId = endpoint.Id;
        }

        if (subscriptions.Count > 0)
        {
            await _dbContext.WebhookEndpointSubscriptions.AddRangeAsync(subscriptions, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return endpoint;
    }

    public async Task<WebhookEndpoint> UpdateAsync(WebhookEndpoint endpoint, CancellationToken cancellationToken)
    {
        if (_dbContext.Entry(endpoint).State == EntityState.Detached)
        {
            _dbContext.WebhookEndpoints.Update(endpoint);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return endpoint;
    }

    public async Task ArchiveAsync(
        Guid? tenantId,
        Guid endpointId,
        DateTime archivedAt,
        CancellationToken cancellationToken)
    {
        await _dbContext.WebhookEndpoints
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .Where(e => e.Id == endpointId &&
                (e.TenantId == tenantId || e.TenantId == null && e.InstanceId != null))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.StatusId, (int)WebhookEndpointStatus.Archived)
                .SetProperty(e => e.DeliveryStateVersion, e => e.DeliveryStateVersion + 1)
                .SetProperty(e => e.UpdatedAt, archivedAt), cancellationToken);
    }

    public async Task<IReadOnlyList<WebhookEndpoint>> GetActiveSubscribedEndpointsAsync(
        Guid tenantId,
        string eventTypeName,
        WebhookProviderMode providerMode,
        CancellationToken cancellationToken)
    {
        var includeComposite = providerMode is WebhookProviderMode.Local or WebhookProviderMode.Svix;

        return await _dbContext.WebhookEndpoints
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .Include(e => e.Consumer)
            .Include(e => e.Subscriptions)
            .ThenInclude(e => e.EventType)
            .Where(e => e.TenantId == tenantId
                && e.StatusId == (int)WebhookEndpointStatus.Active
                && e.Consumer != null
                && (e.Consumer.ProviderModeId == (int)providerMode
                    || (includeComposite && e.Consumer.ProviderModeId == (int)WebhookProviderMode.Composite))
                && e.Consumer.StatusId == (int)WebhookConsumerStatus.Active
                && e.Subscriptions.Any(subscription => subscription.TenantId == tenantId
                    && subscription.IsEnabled
                    && subscription.EventType != null
                    && subscription.EventType.Name == eventTypeName
                    && subscription.EventType.IsEnabled))
            .OrderBy(e => e.CreatedAt)
            .ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WebhookEndpoint>> GetActiveSubscribedEndpointsByConsumerAsync(
        Guid? tenantId,
        Guid consumerId,
        string eventTypeName,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || consumerId == Guid.Empty || string.IsNullOrWhiteSpace(eventTypeName))
        {
            return [];
        }

        return await _dbContext.WebhookEndpoints
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .Include(endpoint => endpoint.Subscriptions)
            .ThenInclude(subscription => subscription.EventType)
            .Where(endpoint =>
                endpoint.TenantId == tenantId &&
                endpoint.ConsumerId == consumerId &&
                endpoint.StatusId == (int)WebhookEndpointStatus.Active &&
                endpoint.Subscriptions.Any(subscription =>
                    subscription.TenantId == tenantId &&
                    subscription.IsEnabled &&
                    subscription.EventType != null &&
                    subscription.EventType.IsEnabled &&
                    subscription.EventType.Name == eventTypeName))
            .OrderBy(endpoint => endpoint.CreatedAt)
            .ThenBy(endpoint => endpoint.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> HasActiveSubscribedEndpointByConsumerAsync(
        Guid? tenantId,
        Guid consumerId,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || consumerId == Guid.Empty)
        {
            return Task.FromResult(false);
        }

        return _dbContext.WebhookEndpoints
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .AnyAsync(endpoint =>
                endpoint.TenantId == tenantId &&
                endpoint.ConsumerId == consumerId &&
                endpoint.StatusId == (int)WebhookEndpointStatus.Active &&
                endpoint.Subscriptions.Any(subscription =>
                    subscription.TenantId == tenantId && subscription.IsEnabled),
                cancellationToken);
    }

    public async Task<IReadOnlyList<WebhookLocalTargetSnapshot>> GetEligiblePendingTargetsForUpdateAsync(
        Guid? tenantId,
        Guid endpointId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.WebhookLocalTargetSnapshots
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .Where(target =>
                (!tenantId.HasValue || target.TenantId == tenantId.Value) &&
                target.WebhookEndpointId == endpointId &&
                target.DeliveryStatusId == (int)WebhookLocalDeliveryStatus.Pending &&
                target.ProcessingLeaseToken == null &&
                target.ProcessingLeaseExpiresAtUtc == null &&
                target.DeliveryFence == 0 &&
                !_dbContext.WebhookDeliveryAttempts.Any(attempt =>
                    (!tenantId.HasValue || attempt.TenantId == tenantId.Value) &&
                    attempt.MessageId == target.WebhookMessageId &&
                    attempt.EndpointId == endpointId))
            .OrderBy(target => target.CapturedAtUtc)
            .ThenBy(target => target.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryPauseAsync(
        Guid? tenantId,
        Guid endpointId,
        long expectedDeliveryStateVersion,
        DateTime pausedAt,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var updated = await _dbContext.WebhookEndpoints
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .Where(e => e.TenantId == tenantId
                && e.Id == endpointId
                && e.DeliveryStateVersion == expectedDeliveryStateVersion
                && e.Consumer != null
                && (e.Consumer.ProviderModeId == (int)WebhookProviderMode.Local
                    || e.Consumer.ProviderModeId == (int)WebhookProviderMode.Composite)
                && e.StatusId == (int)WebhookEndpointStatus.Active)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.StatusId, (int)WebhookEndpointStatus.Disabled)
                .SetProperty(e => e.DeliveryStateVersion, e => e.DeliveryStateVersion + 1)
                .SetProperty(e => e.UpdatedAt, pausedAt)
                .SetProperty(e => e.UpdatedBy, actorUserId), cancellationToken);

        return updated == 1;
    }

    public async Task MarkSuccessAsync(
        Guid tenantId,
        Guid endpointId,
        DateTime succeededAt,
        CancellationToken cancellationToken)
    {
        await _dbContext.WebhookEndpoints
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .Where(e => e.Id == endpointId &&
                (e.TenantId == tenantId || e.TenantId == null && e.InstanceId != null))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.LastSuccessAt, succeededAt)
                .SetProperty(
                    e => e.ConsecutiveFailureCount,
                    e => e.StatusId == (int)WebhookEndpointStatus.Active ? 0 : e.ConsecutiveFailureCount)
                .SetProperty(
                    e => e.DeliveryStateVersion,
                    e => e.StatusId == (int)WebhookEndpointStatus.Active
                        ? e.DeliveryStateVersion + 1
                        : e.DeliveryStateVersion)
                .SetProperty(e => e.UpdatedAt, succeededAt), cancellationToken);
    }

    public async Task<WebhookEndpointFailureState> RecordFailureAsync(
        Guid tenantId,
        Guid endpointId,
        Guid localTargetId,
        Guid leaseToken,
        long deliveryFence,
        DateTime failedAt,
        string failureCategory,
        int autoPauseThreshold,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(autoPauseThreshold, 1);
        var normalizedFailureCategory = Truncate(failureCategory, 100);
        var failedAtUtc = new DateTimeOffset(DateTime.SpecifyKind(failedAt, DateTimeKind.Utc));

        var updated = await _dbContext.WebhookEndpoints
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .Where(e => e.Id == endpointId
                && (e.TenantId == tenantId || e.TenantId == null && e.InstanceId != null)
                && e.StatusId == (int)WebhookEndpointStatus.Active
                && _dbContext.WebhookLocalTargetSnapshots
                    .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
                    .Any(target =>
                        target.TenantId == tenantId
                        && target.Id == localTargetId
                        && target.WebhookEndpointId == endpointId
                        && target.DeliveryStatusId == (int)WebhookLocalDeliveryStatus.Delivering
                        && target.ProcessingLeaseToken == leaseToken
                        && target.DeliveryFence == deliveryFence
                        && target.ProcessingLeaseExpiresAtUtc > failedAtUtc))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.LastFailureAt, failedAt)
                .SetProperty(e => e.ConsecutiveFailureCount, e => e.ConsecutiveFailureCount + 1)
                .SetProperty(
                    e => e.StatusId,
                    e => e.ConsecutiveFailureCount + 1 >= autoPauseThreshold
                        ? (int)WebhookEndpointStatus.AutoPaused
                        : e.StatusId)
                .SetProperty(
                    e => e.CircuitOpenedAt,
                    e => e.ConsecutiveFailureCount + 1 >= autoPauseThreshold
                        ? failedAt
                        : e.CircuitOpenedAt)
                .SetProperty(
                    e => e.AutoPausedAt,
                    e => e.ConsecutiveFailureCount + 1 >= autoPauseThreshold
                        ? failedAt
                        : e.AutoPausedAt)
                .SetProperty(
                    e => e.AutoPauseReason,
                    e => e.ConsecutiveFailureCount + 1 >= autoPauseThreshold
                        ? normalizedFailureCategory
                        : e.AutoPauseReason)
                .SetProperty(e => e.DeliveryStateVersion, e => e.DeliveryStateVersion + 1)
                .SetProperty(e => e.UpdatedAt, failedAt), cancellationToken);

        var state = await _dbContext.WebhookEndpoints
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .Where(e => e.Id == endpointId &&
                (e.TenantId == tenantId || e.TenantId == null && e.InstanceId != null))
            .Select(e => new WebhookEndpointFailureState(
                e.ConsecutiveFailureCount,
                e.StatusId == (int)WebhookEndpointStatus.AutoPaused,
                updated == 1 && e.StatusId == (int)WebhookEndpointStatus.AutoPaused))
            .SingleOrDefaultAsync(cancellationToken);

        return state ?? new WebhookEndpointFailureState(0, false);
    }

    public async Task<bool> TryResumeAsync(
        Guid? tenantId,
        Guid endpointId,
        long expectedDeliveryStateVersion,
        DateTime resumedAt,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var updated = await _dbContext.WebhookEndpoints
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .Where(e => e.TenantId == tenantId
                && e.Id == endpointId
                && e.DeliveryStateVersion == expectedDeliveryStateVersion
                && e.Consumer != null
                && (e.Consumer.ProviderModeId == (int)WebhookProviderMode.Local
                    || e.Consumer.ProviderModeId == (int)WebhookProviderMode.Composite)
                && (e.StatusId == (int)WebhookEndpointStatus.AutoPaused
                    || e.StatusId == (int)WebhookEndpointStatus.Disabled))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.StatusId, (int)WebhookEndpointStatus.Active)
                .SetProperty(e => e.ConsecutiveFailureCount, 0)
                .SetProperty(e => e.CircuitOpenedAt, (DateTime?)null)
                .SetProperty(e => e.AutoPausedAt, (DateTime?)null)
                .SetProperty(e => e.AutoPauseReason, (string?)null)
                .SetProperty(e => e.LastResumedAt, resumedAt)
                .SetProperty(e => e.LastResumedBy, actorUserId)
                .SetProperty(e => e.UpdatedAt, resumedAt)
                .SetProperty(e => e.UpdatedBy, actorUserId)
                .SetProperty(e => e.DeliveryStateVersion, e => e.DeliveryStateVersion + 1),
                cancellationToken);

        return updated == 1;
    }

    private static IQueryable<WebhookEndpoint> ApplyOwnerPredicate(
        IQueryable<WebhookEndpoint> query,
        WebhookOwnershipScope ownership) => ownership.Kind switch
        {
            WebhookConsumerKind.Instance => query.Where(endpoint =>
                endpoint.InstanceId == ownership.InstanceId &&
                endpoint.TenantId == null &&
                endpoint.Consumer != null &&
                endpoint.Consumer.ConsumerKindId == (int)WebhookConsumerKind.Instance),
            WebhookConsumerKind.Tenant => query.Where(endpoint =>
                endpoint.TenantId == ownership.TenantId &&
                endpoint.Consumer != null &&
                endpoint.Consumer.ConsumerKindId == (int)WebhookConsumerKind.Tenant),
            WebhookConsumerKind.Organization => query.Where(endpoint =>
                endpoint.TenantId == ownership.TenantId &&
                endpoint.Consumer != null &&
                endpoint.Consumer.ConsumerKindId == (int)WebhookConsumerKind.Organization &&
                endpoint.Consumer.OrganizationId == ownership.OrganizationId),
            WebhookConsumerKind.Group => query.Where(endpoint =>
                endpoint.TenantId == ownership.TenantId &&
                endpoint.Consumer != null &&
                endpoint.Consumer.ConsumerKindId == (int)WebhookConsumerKind.Group &&
                endpoint.Consumer.GroupId == ownership.GroupId),
            WebhookConsumerKind.User => query.Where(endpoint =>
                endpoint.TenantId == ownership.TenantId &&
                endpoint.Consumer != null &&
                endpoint.Consumer.ConsumerKindId == (int)WebhookConsumerKind.User &&
                endpoint.Consumer.OwnerUserId == ownership.UserId),
            _ => query.Where(_ => false)
        };

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
