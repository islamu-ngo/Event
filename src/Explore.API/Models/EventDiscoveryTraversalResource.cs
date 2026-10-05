using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.Hateoas;

namespace Explore.API.Models;

/// <summary>Exact bounded membership with forward-only HAL continuation, not a live total or page count.</summary>
public sealed record EventDiscoveryTraversalResource(
    int SnapshotCount,
    bool Truncated,
    DateTimeOffset ExpiresAt,
    bool HasMore,
    [property: JsonPropertyName("_links")] ImmutableDictionary<string, HalLink> Links,
    [property: JsonPropertyName("_embedded")] HalCollectionEmbedded<EventDiscoveryItemDto> Embedded);
