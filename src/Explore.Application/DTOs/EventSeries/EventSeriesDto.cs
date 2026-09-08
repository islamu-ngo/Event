using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Explore.Application.DTOs.Event;

namespace Explore.Application.DTOs.EventSeries;

public sealed record EventSeriesDto
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid ConcurrencyStamp { get; init; }
    public required string Title { get; init; }
    public string? Slug { get; init; }
    public string? Description { get; init; }
    public Guid? FeaturedImageId { get; init; }
    public string? FeaturedImageUri { get; init; }
    public Guid ActorId { get; init; }
    public string? ActorDisplayName { get; init; }
    public bool IsPublished { get; init; }
    public DateTimeOffset? StartDateUtc { get; init; }
    public DateTimeOffset? EndDateUtc { get; init; }

    private IReadOnlyList<EventListDto>? _events = ImmutableArray<EventListDto>.Empty;

    public IReadOnlyList<EventListDto> Events
    {
        get => _events!;
        init => _events = value?.ToImmutableArray();
    }
}
