using System.Collections.ObjectModel;
using System.Text.Json;

namespace Explore.Application.DTOs.Analytics;

public sealed record RelayAnalyticsEventDto
{
    private IReadOnlyDictionary<string, JsonElement> _properties =
        new ReadOnlyDictionary<string, JsonElement>(new Dictionary<string, JsonElement>(StringComparer.Ordinal));
    private IReadOnlyDictionary<string, JsonElement> _traits =
        new ReadOnlyDictionary<string, JsonElement>(new Dictionary<string, JsonElement>(StringComparer.Ordinal));

    public string EventType { get; init; } = "pageview";
    public string EventName { get; init; } = string.Empty;
    public string PagePath { get; init; } = string.Empty;
    public string DistinctId { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, JsonElement> Properties
    {
        get => _properties;
        init => _properties = value is null
            ? null!
            : new ReadOnlyDictionary<string, JsonElement>(new Dictionary<string, JsonElement>(value, StringComparer.Ordinal));
    }

    public IReadOnlyDictionary<string, JsonElement> Traits
    {
        get => _traits;
        init => _traits = value is null
            ? null!
            : new ReadOnlyDictionary<string, JsonElement>(new Dictionary<string, JsonElement>(value, StringComparer.Ordinal));
    }
}
