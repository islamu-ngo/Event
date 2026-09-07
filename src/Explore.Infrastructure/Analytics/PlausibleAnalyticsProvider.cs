using System.Text.Json.Serialization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models;
using Microsoft.Extensions.Logging;
using Refit;

namespace Explore.Infrastructure.Analytics;

public class PlausibleAnalyticsProvider : IAnalyticsProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAnalyticsConfigResolver _configResolver;
    private readonly ILogger<PlausibleAnalyticsProvider> _logger;

    private const string DefaultPlausibleHost = "https://plausible.io";

    public PlausibleAnalyticsProvider(
        IHttpClientFactory httpClientFactory,
        IAnalyticsConfigResolver configResolver,
        ILogger<PlausibleAnalyticsProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configResolver = configResolver;
        _logger = logger;
    }

    private IPlausibleApi CreateApi(AnalyticsConfiguration config)
    {
        var client = _httpClientFactory.CreateClient("PlausibleClient");
        var host = string.IsNullOrWhiteSpace(config.EndpointUrl) ? DefaultPlausibleHost : config.EndpointUrl;
        client.BaseAddress = new Uri(host.TrimEnd('/'));
        return RestService.For<IPlausibleApi>(client);
    }

    public Task IdentifyAsync(string distinctId, IDictionary<string, object>? traits = null, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task TrackAsync(string distinctId, string eventName, IDictionary<string, object>? properties = null, CancellationToken cancellationToken = default)
    {
        return SendEventAsync(eventName, properties, cancellationToken);
    }

    public Task PageViewAsync(string distinctId, string pagePath, IDictionary<string, object>? properties = null, CancellationToken cancellationToken = default)
    {
        var payload = properties is null
            ? new Dictionary<string, object>()
            : new Dictionary<string, object>(properties);
        payload["url"] = pagePath;
        return SendEventAsync("pageview", payload, cancellationToken);
    }

    public Task GroupIdentifyAsync(string groupType, string groupKey, IDictionary<string, object>? properties = null, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    private async Task SendEventAsync(string eventName, IDictionary<string, object>? properties, CancellationToken cancellationToken)
    {
        try
        {
            var config = await _configResolver.ResolveAsync(cancellationToken);
            if (!IsActive(config))
            {
                return;
            }

            var requestPayload = new PlausibleEventRequest
            {
                Name = eventName,
                Domain = config.ApiKey,
                Url = properties is not null && properties.TryGetValue("url", out var value) ? value?.ToString() ?? "/" : "/",
                Props = properties ?? new Dictionary<string, object>()
            };

            var api = CreateApi(config);
            var response = await api.SendEventAsync(requestPayload, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Plausible call returned status {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Plausible analytics call failed for event {EventName}", eventName);
        }
    }

    private static bool IsActive(AnalyticsConfiguration config)
    {
        return config.Provider == Explore.Domain.Enums.AnalyticsProviderEnum.Plausible
            && config.IsEnabled
            && !string.IsNullOrWhiteSpace(config.ApiKey);
    }
}

internal interface IPlausibleApi
{
    [Post("/api/event")]
    Task<IApiResponse> SendEventAsync([Body] PlausibleEventRequest request, CancellationToken cancellationToken = default);
}

internal class PlausibleEventRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("domain")]
    public string Domain { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("props")]
    public IDictionary<string, object> Props { get; set; } = new Dictionary<string, object>();
}
