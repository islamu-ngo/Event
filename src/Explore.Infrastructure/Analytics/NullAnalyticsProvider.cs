using Explore.Application.Contracts.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Analytics;

/// <summary>
/// No-op analytics provider. All tracking calls are silently ignored.
/// Used when analytics is disabled or as a safe fallback on provider errors.
/// </summary>
public class NullAnalyticsProvider : IAnalyticsProvider, IAnalyticsFeatureFlagProvider
{
    private readonly ILogger<NullAnalyticsProvider> _logger;

    public NullAnalyticsProvider(ILogger<NullAnalyticsProvider> logger)
    {
        _logger = logger;
    }

    public Task IdentifyAsync(string distinctId, IDictionary<string, object>? traits = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("analytics_identify_disabled");
        return Task.CompletedTask;
    }

    public Task TrackAsync(string distinctId, string eventName, IDictionary<string, object>? properties = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("analytics_track_disabled");
        return Task.CompletedTask;
    }

    public Task PageViewAsync(string distinctId, string pagePath, IDictionary<string, object>? properties = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("analytics_pageview_disabled");
        return Task.CompletedTask;
    }

    public Task GroupIdentifyAsync(string groupType, string groupKey, IDictionary<string, object>? properties = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("analytics_group_identify_disabled");
        return Task.CompletedTask;
    }

    public Task<bool> IsFeatureEnabledAsync(string featureKey, string distinctId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task<object?> GetFeatureFlagPayloadAsync(string featureKey, string distinctId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<object?>(null);
    }
}
