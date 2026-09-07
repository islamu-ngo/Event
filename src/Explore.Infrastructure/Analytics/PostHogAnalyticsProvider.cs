using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models;
using Explore.Domain.Enums;
using Microsoft.Extensions.Logging;
using Refit;

namespace Explore.Infrastructure.Analytics;

/// <summary>
/// PostHog analytics provider using the PostHog HTTP Capture and Decide APIs.
/// Implements both event tracking and feature flag evaluation.
/// All calls are fire-and-forget safe — errors are caught, logged, and swallowed.
/// </summary>
public class PostHogAnalyticsProvider : IAnalyticsProvider, IAnalyticsFeatureFlagProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAnalyticsConfigResolver _configResolver;
    private readonly ILogger<PostHogAnalyticsProvider> _logger;

    private const string DefaultPostHogHost = "https://us.i.posthog.com";

    public PostHogAnalyticsProvider(
        IHttpClientFactory httpClientFactory,
        IAnalyticsConfigResolver configResolver,
        ILogger<PostHogAnalyticsProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configResolver = configResolver;
        _logger = logger;
    }

    private IPostHogApi CreateApi(AnalyticsConfiguration config)
    {
        var client = _httpClientFactory.CreateClient("PostHogClient");
        client.BaseAddress = new Uri(BuildUrl(config, ""));
        return RestService.For<IPostHogApi>(client);
    }

    public Task IdentifyAsync(string distinctId, IDictionary<string, object>? traits = null, CancellationToken cancellationToken = default)
    {
        var properties = new Dictionary<string, object>
        {
            ["$set"] = traits ?? new Dictionary<string, object>()
        };

        return CaptureAsync(distinctId, "$identify", properties, cancellationToken);
    }

    public Task TrackAsync(string distinctId, string eventName, IDictionary<string, object>? properties = null, CancellationToken cancellationToken = default)
    {
        return CaptureAsync(distinctId, eventName, properties, cancellationToken);
    }

    public Task PageViewAsync(string distinctId, string pagePath, IDictionary<string, object>? properties = null, CancellationToken cancellationToken = default)
    {
        var payload = properties is null
            ? new Dictionary<string, object>()
            : new Dictionary<string, object>(properties);
        payload["$current_url"] = pagePath;

        return CaptureAsync(distinctId, "$pageview", payload, cancellationToken);
    }

    public Task GroupIdentifyAsync(string groupType, string groupKey, IDictionary<string, object>? properties = null, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object>
        {
            ["$group_type"] = groupType,
            ["$group_key"] = groupKey
        };

        if (properties is not null)
        {
            payload["$group_set"] = properties;
        }

        return CaptureAsync(groupKey, "$groupidentify", payload, cancellationToken);
    }

    public async Task<bool> IsFeatureEnabledAsync(string featureKey, string distinctId, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = await _configResolver.ResolveAsync(cancellationToken);
            if (!IsActive(config) || string.IsNullOrWhiteSpace(config.PersonalApiKey))
            {
                return false;
            }

            var api = CreateApi(config);
            var request = new PostHogDecideRequest
            {
                ApiKey = config.ApiKey,
                DistinctId = distinctId,
                Groups = new { }
            };

            var response = await api.DecideAsync($"Bearer {config.PersonalApiKey}", request, cancellationToken);
            if (!response.IsSuccessStatusCode || response.Content == null)
            {
                return false;
            }

            if (!response.Content.FeatureFlags.TryGetValue(featureKey, out var featureValue))
            {
                return false;
            }

            if (featureValue is JsonElement element && element.ValueKind == JsonValueKind.True)
            {
                return true;
            }
            if (featureValue is bool b && b)
            {
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PostHog feature flag check failed for {FeatureKey}", featureKey);
            return false;
        }
    }

    public async Task<object?> GetFeatureFlagPayloadAsync(string featureKey, string distinctId, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = await _configResolver.ResolveAsync(cancellationToken);
            if (!IsActive(config) || string.IsNullOrWhiteSpace(config.PersonalApiKey))
            {
                return null;
            }

            var api = CreateApi(config);
            var request = new PostHogDecideRequest
            {
                ApiKey = config.ApiKey,
                DistinctId = distinctId,
                Groups = new { }
            };

            var response = await api.DecideAsync($"Bearer {config.PersonalApiKey}", request, cancellationToken);
            if (!response.IsSuccessStatusCode || response.Content == null)
            {
                return null;
            }

            if (!response.Content.FeatureFlagPayloads.TryGetValue(featureKey, out var payloadValue))
            {
                return null;
            }

            if (payloadValue is JsonElement element)
            {
                return element.GetRawText();
            }

            return JsonSerializer.Serialize(payloadValue);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PostHog feature flag payload fetch failed for {FeatureKey}", featureKey);
            return null;
        }
    }

    private async Task CaptureAsync(string distinctId, string eventName, IDictionary<string, object>? properties, CancellationToken cancellationToken)
    {
        try
        {
            var config = await _configResolver.ResolveAsync(cancellationToken);
            if (!IsActive(config))
            {
                return;
            }

            var payload = new PostHogCaptureRequest
            {
                ApiKey = config.ApiKey,
                Event = eventName,
                DistinctId = distinctId,
                Properties = properties ?? new Dictionary<string, object>()
            };

            var api = CreateApi(config);
            var response = await api.CaptureAsync(payload, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("PostHog capture returned status {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PostHog analytics call failed for event {EventName}", eventName);
        }
    }

    private static bool IsActive(AnalyticsConfiguration config)
    {
        return config.Provider == AnalyticsProviderEnum.Posthog
            && config.IsEnabled
            && !string.IsNullOrWhiteSpace(config.ApiKey);
    }

    private static string BuildUrl(AnalyticsConfiguration config, string path)
    {
        var baseUrl = string.IsNullOrWhiteSpace(config.EndpointUrl) ? DefaultPostHogHost : config.EndpointUrl;
        return $"{baseUrl.TrimEnd('/')}{path}";
    }
}

internal interface IPostHogApi
{
    [Post("/capture/")]
    Task<IApiResponse> CaptureAsync([Body] PostHogCaptureRequest request, CancellationToken cancellationToken = default);

    [Post("/decide/?v=3")]
    Task<IApiResponse<PostHogDecideResponse>> DecideAsync([Header("Authorization")] string authorization, [Body] PostHogDecideRequest request, CancellationToken cancellationToken = default);
}

internal class PostHogCaptureRequest
{
    [JsonPropertyName("api_key")]
    public string ApiKey { get; set; } = string.Empty;

    [JsonPropertyName("event")]
    public string Event { get; set; } = string.Empty;

    [JsonPropertyName("distinct_id")]
    public string DistinctId { get; set; } = string.Empty;

    [JsonPropertyName("properties")]
    public IDictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();
}

internal class PostHogDecideRequest
{
    [JsonPropertyName("api_key")]
    public string ApiKey { get; set; } = string.Empty;

    [JsonPropertyName("distinct_id")]
    public string DistinctId { get; set; } = string.Empty;

    [JsonPropertyName("groups")]
    public object Groups { get; set; } = new { };
}

internal class PostHogDecideResponse
{
    [JsonPropertyName("featureFlags")]
    public Dictionary<string, object> FeatureFlags { get; set; } = new();

    [JsonPropertyName("featureFlagPayloads")]
    public Dictionary<string, object> FeatureFlagPayloads { get; set; } = new();
}
