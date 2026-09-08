using System;

namespace Explore.Application.DTOs.EventSeries;

public sealed record CreateEventSeriesDto
{
    public required string Title { get; init; }
    public string? Description { get; init; }
    public string? Slug { get; init; }
    public Guid? FeaturedImageId { get; init; }
    public Guid ActorId { get; init; }
    public bool IsPublished { get; init; }
}
