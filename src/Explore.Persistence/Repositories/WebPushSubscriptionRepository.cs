using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Explore.Persistence.Repositories;

public sealed class WebPushSubscriptionRepository
    : GenericRepository<WebPushSubscription, Guid>, IWebPushSubscriptionRepository
{
    private readonly ExploreDbContext _dbContext;

    public WebPushSubscriptionRepository(ExploreDbContext dbContext)
        : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<WebPushSubscription> UpsertAsync(
        Guid tenantId,
        Guid userId,
        string deviceIdentifier,
        string endpoint,
        string p256Dh,
        string authSecret,
        DateTime? expirationTime,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var normalizedDevice = deviceIdentifier.Trim();
        var normalizedEndpoint = endpoint.Trim();

        var endpointOwner = await _dbContext.WebPushSubscriptions
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebPushSubscriptionEndpointOwnership)
            .AsNoTracking()
            .FirstOrDefaultAsync(subscription => subscription.IsActive
                && subscription.Endpoint == normalizedEndpoint,
                cancellationToken);

        if (endpointOwner is not null
            && (endpointOwner.TenantId != tenantId || endpointOwner.UserId != userId || endpointOwner.DeviceIdentifier != normalizedDevice))
        {
            throw new InvalidOperationException("Web Push endpoint is already owned by another active device.");
        }

        var subscription = await _dbContext.WebPushSubscriptions
            .FirstOrDefaultAsync(row => row.TenantId == tenantId
                && row.UserId == userId
                && row.DeviceIdentifier == normalizedDevice
                && row.IsActive,
                cancellationToken);

        if (subscription is null)
        {
            subscription = WebPushSubscription.Create(
                tenantId,
                userId,
                normalizedDevice,
                normalizedEndpoint,
                p256Dh,
                authSecret,
                expirationTime,
                now);
            _dbContext.WebPushSubscriptions.Add(subscription);
        }
        else
        {
            subscription.Touch(normalizedEndpoint, p256Dh, authSecret, expirationTime, now);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            subscription = await RecoverFromConcurrentInsertAsync(
                tenantId,
                userId,
                normalizedDevice,
                normalizedEndpoint,
                p256Dh,
                authSecret,
                expirationTime,
                now,
                cancellationToken);
        }

        return subscription;
    }

    private async Task<WebPushSubscription> RecoverFromConcurrentInsertAsync(
        Guid tenantId,
        Guid userId,
        string normalizedDevice,
        string normalizedEndpoint,
        string p256Dh,
        string authSecret,
        DateTime? expirationTime,
        DateTime now,
        CancellationToken cancellationToken)
    {
        foreach (var entry in _dbContext.ChangeTracker.Entries<WebPushSubscription>().Where(entry => entry.State == EntityState.Added))
        {
            entry.State = EntityState.Detached;
        }

        var endpointOwner = await _dbContext.WebPushSubscriptions
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebPushSubscriptionEndpointOwnership)
            .FirstOrDefaultAsync(subscription => subscription.IsActive
                && subscription.Endpoint == normalizedEndpoint,
                cancellationToken);

        if (endpointOwner is not null)
        {
            if (endpointOwner.TenantId != tenantId || endpointOwner.UserId != userId || endpointOwner.DeviceIdentifier != normalizedDevice)
            {
                throw new InvalidOperationException("Web Push endpoint is already owned by another active device.");
            }

            if (now > endpointOwner.LastSeenAt)
            {
                endpointOwner.Touch(normalizedEndpoint, p256Dh, authSecret, expirationTime, now);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return endpointOwner;
        }

        var deviceOwner = await _dbContext.WebPushSubscriptions
            .FirstOrDefaultAsync(subscription => subscription.TenantId == tenantId
                && subscription.UserId == userId
                && subscription.DeviceIdentifier == normalizedDevice
                && subscription.IsActive,
                cancellationToken);

        if (deviceOwner is null)
        {
            throw new InvalidOperationException("Web Push subscription could not be recovered after a concurrent upsert.");
        }

        if (now > deviceOwner.LastSeenAt)
        {
            deviceOwner.Touch(normalizedEndpoint, p256Dh, authSecret, expirationTime, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return deviceOwner;
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    public Task<WebPushSubscription?> GetActiveForDeviceAsync(
        Guid tenantId,
        Guid userId,
        string deviceIdentifier,
        CancellationToken cancellationToken = default)
    {
        var normalizedDevice = deviceIdentifier.Trim();
        return _dbContext.WebPushSubscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(subscription => subscription.TenantId == tenantId
                && subscription.UserId == userId
                && subscription.DeviceIdentifier == normalizedDevice
                && subscription.IsActive,
                cancellationToken);
    }

    public Task<WebPushSubscription?> GetActiveByEndpointAsync(
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        var normalizedEndpoint = endpoint.Trim();
        return _dbContext.WebPushSubscriptions
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebPushSubscriptionEndpointOwnership)
            .AsNoTracking()
            .FirstOrDefaultAsync(subscription => subscription.Endpoint == normalizedEndpoint && subscription.IsActive, cancellationToken);
    }

    public Task<WebPushSubscription?> GetActiveByIdAsync(
        Guid tenantId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.WebPushSubscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(subscription => subscription.TenantId == tenantId
                && subscription.Id == subscriptionId
                && subscription.IsActive,
                cancellationToken);
    }

    public async Task<IReadOnlyList<WebPushSubscription>> ListActiveForUserAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.WebPushSubscriptions
            .AsNoTracking()
            .Where(subscription => subscription.TenantId == tenantId
                && subscription.UserId == userId
                && subscription.IsActive)
            .OrderBy(subscription => subscription.CreatedAt)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<bool> UnsubscribeAsync(
        Guid tenantId,
        Guid userId,
        Guid subscriptionId,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var updated = await _dbContext.WebPushSubscriptions
            .Where(subscription => subscription.TenantId == tenantId
                && subscription.UserId == userId
                && subscription.Id == subscriptionId
                && subscription.IsActive)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(subscription => subscription.IsActive, false)
                .SetProperty(subscription => subscription.UnsubscribedAt, now)
                .SetProperty(subscription => subscription.UpdatedAt, now), cancellationToken);

        return updated > 0;
    }

    public async Task<bool> UnsubscribeDeviceAsync(
        Guid tenantId,
        Guid userId,
        string deviceIdentifier,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var normalizedDevice = deviceIdentifier.Trim();
        var updated = await _dbContext.WebPushSubscriptions
            .Where(subscription => subscription.TenantId == tenantId
                && subscription.UserId == userId
                && subscription.DeviceIdentifier == normalizedDevice
                && subscription.IsActive)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(subscription => subscription.IsActive, false)
                .SetProperty(subscription => subscription.UnsubscribedAt, now)
                .SetProperty(subscription => subscription.UpdatedAt, now), cancellationToken);

        return updated > 0;
    }
}
