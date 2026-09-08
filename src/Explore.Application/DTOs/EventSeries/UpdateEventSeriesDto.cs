using Explore.Application.Models.Common;

namespace Explore.Application.DTOs.EventSeries;

public sealed record UpdateEventSeriesDto
{
    public UpdateEventSeriesTitleDto? Title { get; init; }
    public UpdateEventSeriesDescriptionDto? Description { get; init; }
    public UpdateEventSeriesSlugDto? Slug { get; init; }
    public UpdateEventSeriesFeaturedImageDto? FeaturedImage { get; init; }
    public UpdateEventSeriesPublicationDto? Publication { get; init; }
}

public sealed record UpdateEventSeriesTitleDto
{
    public required string Value { get; init; }
}

public sealed record UpdateEventSeriesDescriptionDto
{
    public OptionalUpdate<string?> Value { get; init; } = OptionalUpdate<string?>.Unspecified();
}

public sealed record UpdateEventSeriesSlugDto
{
    public OptionalUpdate<string?> Value { get; init; } = OptionalUpdate<string?>.Unspecified();
}

public sealed record UpdateEventSeriesFeaturedImageDto
{
    public OptionalUpdate<Guid?> Value { get; init; } = OptionalUpdate<Guid?>.Unspecified();
}

public sealed record UpdateEventSeriesPublicationDto
{
    public bool IsPublished { get; init; }
}
