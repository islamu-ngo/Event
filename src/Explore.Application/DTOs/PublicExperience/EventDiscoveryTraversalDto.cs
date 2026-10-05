using System.Text.Json.Serialization;
using Explore.Application.Features.Events.Discovery;

namespace Explore.Application.DTOs.PublicExperience;

/// <summary>Bounded membership metadata, valid only while the captured authority and clock boundary hold.</summary>
public sealed record EventDiscoveryTraversalDto
{
    private readonly IReadOnlyList<EventDiscoveryItemDto> _items = [];

    public IReadOnlyList<EventDiscoveryItemDto> Items
    {
        get => _items;
        init => _items = Array.AsReadOnly(value.ToArray());
    }

    public int SnapshotCount { get; init; }
    public bool Truncated { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public bool HasMore { get; init; }

    [JsonIgnore]
    public string? NextCursor { get; init; }

    [JsonIgnore]
    public EventDiscoveryReadStamp Authority { get; init; } = default!;
}
