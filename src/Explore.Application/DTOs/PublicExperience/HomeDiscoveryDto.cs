using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Application.DTOs.Event;
using Explore.Application.Models.PublicExperience;

namespace Explore.Application.DTOs.PublicExperience;

public sealed record HomeDiscoveryDto
{
    private IReadOnlyList<EventDiscoveryItemDto> _hero = Array.AsReadOnly(Array.Empty<EventDiscoveryItemDto>());
    private IReadOnlyList<EventDiscoveryItemDto> _upcomingInArea = Array.AsReadOnly(Array.Empty<EventDiscoveryItemDto>());
    private IReadOnlyList<EventDiscoveryItemDto> _mostViewedInArea = Array.AsReadOnly(Array.Empty<EventDiscoveryItemDto>());
    private IReadOnlyList<EventDiscoveryItemDto> _mostViewedOnline = Array.AsReadOnly(Array.Empty<EventDiscoveryItemDto>());
    private IReadOnlyList<HomeDiscoverySectionDto> _curatedSections = Array.AsReadOnly(Array.Empty<HomeDiscoverySectionDto>());
    private IReadOnlyList<EventDiscoveryItemDto> _recentlyAdded = Array.AsReadOnly(Array.Empty<EventDiscoveryItemDto>());
    private IReadOnlyDictionary<string, HomeDiscoverySectionStatus> _sectionStatuses =
        new ReadOnlyDictionary<string, HomeDiscoverySectionStatus>(
            new Dictionary<string, HomeDiscoverySectionStatus>(StringComparer.Ordinal));

    public int SchemaVersion { get; init; } = 1;
    public HomeDiscoveryContextDto Context { get; set; } = new();
    public IReadOnlyList<EventDiscoveryItemDto> Hero
    {
        get => _hero;
        init => _hero = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }

    public IReadOnlyList<EventDiscoveryItemDto> UpcomingInArea
    {
        get => _upcomingInArea;
        init => _upcomingInArea = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }
    public HomeDiscoverySectionDto? Spotlight { get; set; }
    public IReadOnlyList<EventDiscoveryItemDto> MostViewedInArea
    {
        get => _mostViewedInArea;
        init => _mostViewedInArea = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }

    public IReadOnlyList<EventDiscoveryItemDto> MostViewedOnline
    {
        get => _mostViewedOnline;
        init => _mostViewedOnline = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }

    public IReadOnlyList<HomeDiscoverySectionDto> CuratedSections
    {
        get => _curatedSections;
        init => _curatedSections = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }

    public IReadOnlyList<EventDiscoveryItemDto> RecentlyAdded
    {
        get => _recentlyAdded;
        init => _recentlyAdded = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }

    public IReadOnlyDictionary<string, HomeDiscoverySectionStatus> SectionStatuses
    {
        get => _sectionStatuses;
        init => _sectionStatuses = value is null
            ? null!
            : new ReadOnlyDictionary<string, HomeDiscoverySectionStatus>(
                new Dictionary<string, HomeDiscoverySectionStatus>(value, StringComparer.Ordinal));
    }
    public DateTimeOffset GeneratedAtUtc { get; init; }
}

public sealed record HomeDiscoveryContextDto
{
    private IReadOnlyList<PublicDiscoveryAreaDto> _availableAreas =
        Array.AsReadOnly(Array.Empty<PublicDiscoveryAreaDto>());

    public HomeDiscoveryMode Mode { get; init; } = HomeDiscoveryMode.All;
    public Guid? SelectedAreaId { get; init; }
    public Guid? DefaultAreaId { get; init; }
    public string SelectedAreaDisplayName { get; init; } = string.Empty;
    public IReadOnlyList<PublicDiscoveryAreaDto> AvailableAreas
    {
        get => _availableAreas;
        init => _availableAreas = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }
}

public sealed record EventDiscoveryItemDto
{
    [JsonConstructor]
    public EventDiscoveryItemDto()
    {
    }

    public string Source { get; init; } = "local";
    public EventListDto? Event { get; init; }
    public FederatedEventDto? FederatedEvent { get; init; }
    public EventFederationMetadataDto? Federation { get; init; }
    public double? DistanceMeters { get; init; }
    public Guid? NearestSessionId { get; init; }
    public Guid? NearestLocationId { get; init; }
    public string? NearestLocationName { get; init; }
    public DateTimeOffset? NearestOccurrenceStartsAtUtc { get; init; }

    [JsonInclude]
    [JsonExtensionData]
    internal Dictionary<string, JsonElement> JsonAdditionalProperties { get; } = new(StringComparer.Ordinal);

    [JsonIgnore]
    public ImmutableDictionary<string, object> AdditionalProperties
    {
        get => JsonAdditionalProperties.ToImmutableDictionary(
            property => property.Key,
            property => (object)property.Value,
            StringComparer.Ordinal);
        set
        {
            JsonAdditionalProperties.Clear();
            if (value is null)
            {
                return;
            }

            foreach (var property in value)
            {
                JsonAdditionalProperties[property.Key] = JsonSerializer.SerializeToElement(
                    property.Value,
                    property.Value?.GetType() ?? typeof(object));
            }
        }
    }
}

public sealed record FederatedEventDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? StartsAtUtc { get; init; }
    public DateTimeOffset? EndsAtUtc { get; init; }
    public string? Mode { get; init; }
    public string? Status { get; init; }
    public bool? RsvpExpected { get; init; }
    public string? LocationSummary { get; init; }
}

public sealed record EventFederationMetadataDto
{
    public Guid AtprotoRecordId { get; init; }
    public string Provenance { get; init; } = string.Empty;
    public bool IsLocalEcho { get; init; }
    public bool HasSourceLink { get; set; }
}

public sealed record HomeDiscoverySectionDto
{
    private IReadOnlyList<EventDiscoveryItemDto> _items = Array.AsReadOnly(Array.Empty<EventDiscoveryItemDto>());

    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public IReadOnlyList<EventDiscoveryItemDto> Items
    {
        get => _items;
        init => _items = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }
}
