using Explore.Domain.Settings;
using MediatR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Notifications;

public class PolicyChangedCacheInvalidationHandler : INotificationHandler<PolicyChangedNotification>
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<PolicyChangedCacheInvalidationHandler> _logger;

    public PolicyChangedCacheInvalidationHandler(
        IDistributedCache cache,
        ILogger<PolicyChangedCacheInvalidationHandler> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task Handle(PolicyChangedNotification notification, CancellationToken cancellationToken)
    {
        var cacheKey = BuildCacheKey(notification.Scope, notification.ScopeId);

        await _cache.RemoveAsync(cacheKey, cancellationToken);

        _logger.LogInformation(
            "Invalidated policy cache for {Scope} {ScopeId} by {ChangedBy}",
            notification.Scope,
            notification.ScopeId,
            notification.ChangedBy);
    }

    public static string BuildCacheKey(SettingScope scope, Guid? scopeId) =>
        scope switch
        {
            SettingScope.Instance => "policy:instance",
            SettingScope.Tenant => $"policy:tenant:{scopeId}",
            SettingScope.Organization => $"policy:org:{scopeId}",
            _ => $"policy:{scope}:{scopeId}"
        };
}
