using System.Text.Json.Serialization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models;
using Explore.Domain.Enums;
using Microsoft.Extensions.Logging;
using Refit;

namespace Explore.Infrastructure.Analytics;

public class RybbitAnalyticsProvider : IAnalyticsProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAnalyticsConfigResolver _configResolver;
    private readonly ILogger<RybbitAnalyticsProvider> _logger;

    public RybbitAnalyticsProvider(
        IHttpClientFactory httpClientFactory,
        IAnalyticsConfigResolver configResolver,
        ILogger<RybbitAnalyticsProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configResolver = configResolver;
        _logger = logger;
    }

    private IRybbitApi CreateApi(AnalyticsConfiguration config)
    {
        var client = _httpClientFactory.CreateClient("RybbitClient");
        client.BaseAddress = new Uri(config.EndpointUrl!.TrimEnd('/'));
        return RestService.For<IRybbitApi>(client);
    }

    public Task IdentifyAsync(string distinctId, IDictionary<string, object>? traits = null, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task TrackAsync(string distinctId, string eventName, IDictionary<string, object>? properties = null, CancellationToken cancellationToken = default)
    {
        return SendAsync("event", distinctId, eventName, properties, cancellationToken);
    }

    public Task PageViewAsync(string distinctId, string pagePath, IDictionary<string, object>? properties = null, CancellationToken cancellationToken = default)
    {
        var payload = properties is null
            ? new Dictionary<string, object>()
            : new Dictionary<string, object>(properties);
        payload["pagePath"] = pagePath;
        return SendAsync("pageview", distinctId, pagePath, payload, cancellationToken);
    }

    public Task GroupIdentifyAsync(string groupType, string groupKey, IDictionary<string, object>? properties = null, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    private async Task SendAsync(string type, string distinctId, string name, IDictionary<string, object>? properties, CancellationToken cancellationToken)
    {
        try
        {
            var config = await _configResolver.ResolveAsync(cancellationToken);
            if (!IsActive(config) || string.IsNullOrWhiteSpace(config.EndpointUrl))
            {
                return;
            }

            var payload = new RybbitEventRequest
            {
                Type = type,
                Name = name,
                UserId = distinctId,
                SiteId = config.ApiKey,
                Properties = properties ?? new Dictionary<string, object>()
            };

            var api = CreateApi(config);
            var response = await api.SendTrackAsync(payload, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Rybbit call returned status {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rybbit analytics call failed for {Type}:{Name}", type, name);
        }
    }

    private static bool IsActive(AnalyticsConfiguration config)
    {
        return config.Provider == AnalyticsProviderEnum.Rybbit
            && config.IsEnabled
            && !string.IsNullOrWhiteSpace(config.ApiKey);
    }
}

internal interface IRybbitApi
{
    [Post("/api/track")]
    Task<IApiResponse> SendTrackAsync([Body] RybbitEventRequest request, CancellationToken cancellationToken = default);
}

internal class RybbitEventRequest
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("siteId")]
    public string SiteId { get; set; } = string.Empty;

    [JsonPropertyName("properties")]
    public IDictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();
}
