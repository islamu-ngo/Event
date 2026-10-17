using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Analytics;

/// <summary>
/// Authorization provider that delegates to Posthog, Plausible, Rybbit, or Null
/// based on a runtime-switchable SystemSetting.
/// The active provider is cached for 5 minutes to avoid repeated DB queries.
/// Falls back to NullAnalyticsProvider if the resolved provider fails.
/// </summary>
public sealed class RuntimeAnalyticsProvider : IAnalyticsProvider, IAnalyticsFeatureFlagProvider
{
    private readonly PostHogAnalyticsProvider _postHogProvider;
    private readonly PlausibleAnalyticsProvider _plausibleProvider;
    private readonly RybbitAnalyticsProvider _rybbitProvider;
    private readonly RudderStackAnalyticsProvider _rudderStackProvider;
    private readonly NullAnalyticsProvider _nullProvider;
    private readonly IAnalyticsConfigResolver _configResolver;
    private readonly IMemoryCache _cache;
    private readonly ILogger<RuntimeAnalyticsProvider> _logger;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private const string CacheKeyPrefix = "AnalyticsProvider_Resolved:";

    public RuntimeAnalyticsProvider(
        PostHogAnalyticsProvider postHogProvider,
        PlausibleAnalyticsProvider plausibleProvider,
        RybbitAnalyticsProvider rybbitProvider,
        RudderStackAnalyticsProvider rudderStackProvider,
        NullAnalyticsProvider nullProvider,
        IAnalyticsConfigResolver configResolver,
        IMemoryCache cache,
        ILogger<RuntimeAnalyticsProvider> logger)
    {
        _postHogProvider = postHogProvider;
        _plausibleProvider = plausibleProvider;
        _rybbitProvider = rybbitProvider;
        _rudderStackProvider = rudderStackProvider;
        _nullProvider = nullProvider;
        _configResolver = configResolver;
        _cache = cache;
        _logger = logger;
    }

    public async Task IdentifyAsync(string distinctId, IDictionary<string, object>? traits = null, CancellationToken cancellationToken = default)
    {
        var provider = await ResolveProviderAsync(cancellationToken);

        try
        {
            await provider.IdentifyAsync(distinctId, traits, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning("analytics_identify_failed");
        }
    }

    public async Task TrackAsync(string distinctId, string eventName, IDictionary<string, object>? properties = null, CancellationToken cancellationToken = default)
    {
        var provider = await ResolveProviderAsync(cancellationToken);

        try
        {
            await provider.TrackAsync(distinctId, eventName, properties, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning("analytics_track_failed");
        }
    }

    public async Task PageViewAsync(string distinctId, string pagePath, IDictionary<string, object>? properties = null, CancellationToken cancellationToken = default)
    {
        var provider = await ResolveProviderAsync(cancellationToken);

        try
        {
            await provider.PageViewAsync(distinctId, pagePath, properties, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning("analytics_pageview_failed");
        }
    }

    public async Task GroupIdentifyAsync(string groupType, string groupKey, IDictionary<string, object>? properties = null, CancellationToken cancellationToken = default)
    {
        var provider = await ResolveProviderAsync(cancellationToken);

        try
        {
            await provider.GroupIdentifyAsync(groupType, groupKey, properties, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning("analytics_group_identify_failed");
        }
    }

    public async Task<bool> IsFeatureEnabledAsync(string featureKey, string distinctId, CancellationToken cancellationToken = default)
    {
        var provider = await ResolveProviderAsync(cancellationToken);
        if (provider is not IAnalyticsFeatureFlagProvider featureFlagProvider)
        {
            return false;
        }

        try
        {
            return await featureFlagProvider.IsFeatureEnabledAsync(featureKey, distinctId, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning("analytics_feature_flag_check_failed");
            return false;
        }
    }

    public async Task<object?> GetFeatureFlagPayloadAsync(string featureKey, string distinctId, CancellationToken cancellationToken = default)
    {
        var provider = await ResolveProviderAsync(cancellationToken);
        if (provider is not IAnalyticsFeatureFlagProvider featureFlagProvider)
        {
            return null;
        }

        try
        {
            return await featureFlagProvider.GetFeatureFlagPayloadAsync(featureKey, distinctId, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning("analytics_feature_flag_payload_failed");
            return null;
        }
    }

    private async Task<IAnalyticsProvider> ResolveProviderAsync(CancellationToken cancellationToken)
    {
        try
        {
            var config = await _configResolver.ResolveAsync(cancellationToken);
            if (!config.IsEnabled)
            {
                return _nullProvider;
            }

            return config.Provider switch
            {
                AnalyticsProviderEnum.Posthog => _postHogProvider,
                AnalyticsProviderEnum.Plausible => _plausibleProvider,
                AnalyticsProviderEnum.Rybbit => _rybbitProvider,
                AnalyticsProviderEnum.RudderStack => _rudderStackProvider,
                _ => _nullProvider
            };
        }
        catch (Exception)
        {
            _logger.LogWarning("analytics_provider_resolution_failed fallback=disabled");
            return _nullProvider;
        }
    }
}
