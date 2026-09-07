using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models;
using Explore.Domain.Enums;
using Microsoft.Extensions.Logging;
using Refit;

namespace Explore.Infrastructure.Analytics;

public class RudderStackAnalyticsProvider : IAnalyticsProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAnalyticsConfigResolver _configResolver;
    private readonly ILogger<RudderStackAnalyticsProvider> _logger;

    public RudderStackAnalyticsProvider(
        IHttpClientFactory httpClientFactory,
        IAnalyticsConfigResolver configResolver,
        ILogger<RudderStackAnalyticsProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configResolver = configResolver;
        _logger = logger;
    }

    private IRudderStackApi CreateApi(AnalyticsConfiguration config)
    {
        var client = _httpClientFactory.CreateClient("RudderStackClient");
        var endpoint = (config.EndpointUrl ?? string.Empty).TrimEnd('/');
        client.BaseAddress = new Uri(endpoint);
        return RestService.For<IRudderStackApi>(client);
    }

    public Task IdentifyAsync(string distinctId, IDictionary<string, object>? traits = null, CancellationToken cancellationToken = default)
    {
        return SendAsync("/identify", new
        {
            userId = distinctId,
            traits = traits ?? new Dictionary<string, object>()
        }, cancellationToken);
    }

    public Task TrackAsync(string distinctId, string eventName, IDictionary<string, object>? properties = null, CancellationToken cancellationToken = default)
    {
        return SendAsync("/track", new
        {
            userId = distinctId,
            @event = eventName,
            properties = properties ?? new Dictionary<string, object>()
        }, cancellationToken);
    }

    public Task PageViewAsync(string distinctId, string pagePath, IDictionary<string, object>? properties = null, CancellationToken cancellationToken = default)
    {
        return SendAsync("/page", new
        {
            userId = distinctId,
            name = pagePath,
            properties = properties ?? new Dictionary<string, object>()
        }, cancellationToken);
    }

    public Task GroupIdentifyAsync(string groupType, string groupKey, IDictionary<string, object>? properties = null, CancellationToken cancellationToken = default)
    {
        return SendAsync("/group", new
        {
            userId = groupKey,
            groupId = groupKey,
            traits = properties ?? new Dictionary<string, object>(),
            context = new Dictionary<string, object>
            {
                ["groupType"] = groupType
            }
        }, cancellationToken);
    }

    private async Task SendAsync(string path, object payload, CancellationToken cancellationToken)
    {
        try
        {
            var config = await _configResolver.ResolveAsync(cancellationToken);
            if (!IsActive(config))
            {
                return;
            }

            var endpoint = (config.EndpointUrl ?? string.Empty).TrimEnd('/');
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return;
            }

            var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.ApiKey}:"));
            var api = CreateApi(config);

            var response = await api.SendAsync(path.TrimStart('/'), $"Basic {authToken}", payload, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("RudderStack call {Path} returned status {StatusCode}", path, response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RudderStack analytics call failed for {Path}", path);
        }
    }

    private static bool IsActive(AnalyticsConfiguration config)
    {
        return config.Provider == AnalyticsProviderEnum.RudderStack
            && config.IsEnabled
            && !string.IsNullOrWhiteSpace(config.ApiKey);
    }
}

internal interface IRudderStackApi
{
    [Post("/{path}")]
    Task<IApiResponse> SendAsync(string path, [Header("Authorization")] string authorization, [Body] object request, CancellationToken cancellationToken = default);
}
