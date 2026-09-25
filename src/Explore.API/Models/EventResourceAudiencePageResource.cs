using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Explore.Application.DTOs.EventResource;
using Explore.Application.Hateoas;

namespace Explore.API.Models;

/// <summary>Authorized audience items and protected continuation, never candidate counts.</summary>
public sealed record EventResourceAudiencePageResource(
    string? NextCursor,
    [property: JsonPropertyName("_links")] ImmutableDictionary<string, HalLink> Links,
    [property: JsonPropertyName("_embedded")] HalCollectionEmbedded<EventResourceAudienceDetailDto> Embedded);
