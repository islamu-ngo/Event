using System.Text.Json;
using System.Text.RegularExpressions;
using Explore.Application.Analytics;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.PublicExperience.Requests.Commands;
using MediatR;

namespace Explore.Application.Features.PublicExperience.Handlers.Commands;

public partial class RelayAnalyticsEventCommandHandler : IRequestHandler<RelayAnalyticsEventCommand, bool>
{
    private readonly ITenantContext _tenantContext;
    private readonly IAnalyticsProvider _analyticsProvider;
    private readonly IAnalyticsConfigResolver _analyticsConfigResolver;
    private readonly IAnalyticsGovernanceService _analyticsGovernanceService;

    public RelayAnalyticsEventCommandHandler(
        ITenantContext tenantContext,
        IAnalyticsProvider analyticsProvider,
        IAnalyticsConfigResolver analyticsConfigResolver,
        IAnalyticsGovernanceService analyticsGovernanceService)
    {
        _tenantContext = tenantContext;
        _analyticsProvider = analyticsProvider;
        _analyticsConfigResolver = analyticsConfigResolver;
        _analyticsGovernanceService = analyticsGovernanceService;
    }

    public async Task<bool> Handle(RelayAnalyticsEventCommand request, CancellationToken cancellationToken)
    {
        var configuration = await _analyticsConfigResolver.ResolveAsync(cancellationToken);
        if (!configuration.IsEnabled || configuration.Provider == Explore.Domain.Enums.AnalyticsProviderEnum.None)
        {
            return true;
        }

        var payload = request.Payload;
        var distinctId = ResolveDistinctId(payload.DistinctId, request.AuthenticatedUserId);
        var properties = ConvertProperties(payload.Properties);
        properties[AnalyticsEvents.Properties.TenantId] = _tenantContext.TenantId;

        switch (payload.EventType.Trim().ToLowerInvariant())
        {
            case "pageview":
                return await RelayPageViewAsync(configuration, distinctId, payload.PagePath, properties, cancellationToken);

            case "track":
                return await RelayTrackAsync(configuration, distinctId, payload.EventName, properties, cancellationToken);

            default:
                return false;
        }
    }

    private async Task<bool> RelayPageViewAsync(
        Models.AnalyticsConfiguration configuration,
        string distinctId,
        string pagePath,
        Dictionary<string, object?> properties,
        CancellationToken cancellationToken)
    {
        var request = _analyticsGovernanceService.CreatePageViewRequest(configuration, distinctId, pagePath, properties);
        if (request is null)
        {
            return true;
        }

        await _analyticsProvider.PageViewAsync(
            request.DistinctId,
            request.PagePath,
            request.Properties.ToDictionary(x => x.Key, x => x.Value),
            cancellationToken);

        return true;
    }

    private async Task<bool> RelayTrackAsync(
        Models.AnalyticsConfiguration configuration,
        string distinctId,
        string eventName,
        Dictionary<string, object?> properties,
        CancellationToken cancellationToken)
    {
        if (!IsValidClientEventName(eventName))
        {
            return false;
        }

        if (properties.Count > 20)
        {
            return false;
        }

        var definition = new AnalyticsEventDefinition(
            eventName,
            properties.Keys.ToHashSet(StringComparer.Ordinal));

        var request = _analyticsGovernanceService.CreateTrackRequest(configuration, distinctId, definition, properties);
        if (request is null)
        {
            return true;
        }

        await _analyticsProvider.TrackAsync(
            request.DistinctId,
            request.EventName,
            request.Properties.ToDictionary(x => x.Key, x => x.Value),
            cancellationToken);

        return true;
    }

    private static string ResolveDistinctId(string? relayDistinctId, Guid? authenticatedUserId)
    {
        if (!string.IsNullOrWhiteSpace(relayDistinctId))
        {
            return relayDistinctId;
        }

        return authenticatedUserId?.ToString() ?? $"tenant-{Guid.CreateVersion7():N}";
    }

    private static Dictionary<string, object?> ConvertProperties(IReadOnlyDictionary<string, JsonElement> properties)
    {
        var converted = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var (key, value) in properties)
        {
            if (!IsValidPropertyKey(key))
            {
                continue;
            }

            converted[key] = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
                JsonValueKind.Number when value.TryGetDouble(out var floating) => floating,
                JsonValueKind.Array => ConvertStringArray(value),
                _ => null
            };
        }

        return converted;
    }

    private static string[] ConvertStringArray(JsonElement value)
    {
        var results = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var text = item.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    results.Add(text);
                }
            }
        }

        return results.ToArray();
    }

    private static bool IsValidPropertyKey(string key)
    {
        return ClientPropertyKeyRegex().IsMatch(key);
    }

    private static bool IsValidClientEventName(string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
        {
            return false;
        }

        if (!(eventName.StartsWith("ui.", StringComparison.Ordinal) || eventName.StartsWith("public.", StringComparison.Ordinal)))
        {
            return false;
        }

        return ClientEventNameRegex().IsMatch(eventName);
    }

    [GeneratedRegex("^[a-z][a-z0-9_.]{1,79}$", RegexOptions.Compiled)]
    private static partial Regex ClientEventNameRegex();

    [GeneratedRegex("^[a-z][a-z0-9_]{1,63}$", RegexOptions.Compiled)]
    private static partial Regex ClientPropertyKeyRegex();
}
