using System;

namespace Explore.Application.DTOs.EventSeries;

public sealed record EventSeriesListDto
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid ConcurrencyStamp { get; init; }
    public required string Title { get; init; }
    public string? Slug { get; init; }
    public string? Description { get; init; }
    public string? FeaturedImageUri { get; init; }
    public Guid ActorId { get; init; }
    public string? ActorDisplayName { get; init; }
    public DateTimeOffset? StartDateUtc { get; init; }
    public DateTimeOffset? EndDateUtc { get; init; }
    public int EventCount { get; init; }
}
