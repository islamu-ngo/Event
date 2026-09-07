using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Explore.Application.Hateoas;

namespace Explore.Application.DTOs.EventAggregateView;

public sealed record EventWithSessionsViewDto
{
    public Guid EventId { get; init; }
    public Guid TenantId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DateTimeOffset StartAt { get; init; }
    public DateTimeOffset? EndAt { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Visibility { get; init; } = string.Empty;
    public bool IsDeleted { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public string? IslamicTheme { get; init; }
    public string? Madhab { get; init; }
    public bool? IsRamadan { get; init; }
    public bool? PrayerAware { get; init; }
    public string? TechStack { get; init; }
    public string? DifficultyLevel { get; init; }
    public string? TargetAudience { get; init; }
    public int SessionCount { get; init; }
    public DateTimeOffset? FirstSessionStartAt { get; init; }
    public DateTimeOffset? LastSessionEndAt { get; init; }
    public bool HasInPersonSessions { get; init; }
    public bool HasVirtualSessions { get; init; }
    public string? AggregatedSessionIslamicThemes { get; init; }
    private IReadOnlyList<EventCustomPropertyFacetDto>? _eventCustomProperties = ImmutableArray<EventCustomPropertyFacetDto>.Empty;
    private IReadOnlyList<EventSessionCustomPropertyFacetDto>? _eventSessionCustomProperties = ImmutableArray<EventSessionCustomPropertyFacetDto>.Empty;
    private IReadOnlyDictionary<string, HalLink>? _links = ImmutableDictionary<string, HalLink>.Empty;

    public IReadOnlyList<EventCustomPropertyFacetDto> EventCustomProperties
    {
        get => _eventCustomProperties!;
        init => _eventCustomProperties = value?.ToImmutableArray();
    }

    public IReadOnlyList<EventSessionCustomPropertyFacetDto> EventSessionCustomProperties
    {
        get => _eventSessionCustomProperties!;
        init => _eventSessionCustomProperties = value?.ToImmutableArray();
    }

    [JsonPropertyName("_links")]
    public IReadOnlyDictionary<string, HalLink> Links
    {
        get => _links!;
        init => _links = value?.ToImmutableDictionary();
    }
}
