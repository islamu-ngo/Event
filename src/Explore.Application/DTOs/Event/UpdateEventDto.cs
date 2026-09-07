using System;
using Explore.Application.Models.Common;

namespace Explore.Application.DTOs.Event;

public sealed record UpdateEventDto
{
    public UpdateEventTitleDto? Title { get; init; }
    public UpdateEventSubtitleDto? Subtitle { get; init; }
    public UpdateEventDescriptionDto? Description { get; init; }
    public UpdateEventContentDto? Content { get; init; }
    public UpdateEventSlugDto? Slug { get; init; }
    public UpdateEventEventTypeDto? EventType { get; init; }
    public UpdateEventAudienceGenderDto? AudienceGender { get; init; }
    public UpdateEventAudienceAgeDto? AudienceAge { get; init; }
    public UpdateEventFeaturedImageDto? FeaturedImage { get; init; }
    public UpdateEventVisibilityDto? Visibility { get; init; }
    public UpdateEventFormatDto? Format { get; init; }
    public UpdateEventMadhabDto? Madhab { get; init; }
    public UpdateEventTimezoneDto? Timezone { get; init; }
    public UpdateEventEventTimeZoneDto? EventTimeZone { get; init; }
    public UpdateEventBackgroundColorDto? BackgroundColor { get; init; }
    public UpdateEventBackgroundEffectDto? BackgroundEffect { get; init; }
    public UpdateEventBackgroundImageDto? BackgroundImage { get; init; }
    public UpdateEventSourceTemplateDto? SourceTemplate { get; init; }
    public UpdateEventSeriesMembershipDto? SeriesMembership { get; init; }
    public UpdateEventSeriesOrderDto? SeriesOrder { get; init; }
    public UpdateEventRegistrationPolicyDto? RegistrationPolicy { get; init; }
}

public sealed record UpdateEventTitleDto
{
    public required string Value { get; init; }
}

public sealed record UpdateEventSubtitleDto
{
    public OptionalUpdate<string?> Value { get; init; } = OptionalUpdate<string?>.Unspecified();
}

public sealed record UpdateEventDescriptionDto
{
    public OptionalUpdate<string?> Value { get; init; } = OptionalUpdate<string?>.Unspecified();
}

public sealed record UpdateEventContentDto
{
    public OptionalUpdate<string?> Value { get; init; } = OptionalUpdate<string?>.Unspecified();
}

public sealed record UpdateEventSlugDto
{
    public OptionalUpdate<string?> Value { get; init; } = OptionalUpdate<string?>.Unspecified();
}

public sealed record UpdateEventEventTypeDto
{
    public OptionalUpdate<int?> Value { get; init; } = OptionalUpdate<int?>.Unspecified();
}

public sealed record UpdateEventAudienceGenderDto
{
    public OptionalUpdate<int?> Value { get; init; } = OptionalUpdate<int?>.Unspecified();
}

public sealed record UpdateEventAudienceAgeDto
{
    public OptionalUpdate<int?> Value { get; init; } = OptionalUpdate<int?>.Unspecified();
}

public sealed record UpdateEventFeaturedImageDto
{
    public OptionalUpdate<Guid?> Value { get; init; } = OptionalUpdate<Guid?>.Unspecified();
}

public sealed record UpdateEventVisibilityDto
{
    public int Value { get; init; }
}

public sealed record UpdateEventFormatDto
{
    public int Value { get; init; }
}

public sealed record UpdateEventMadhabDto
{
    public OptionalUpdate<int?> Value { get; init; } = OptionalUpdate<int?>.Unspecified();
}

public sealed record UpdateEventTimezoneDto
{
    public OptionalUpdate<string?> Value { get; init; } = OptionalUpdate<string?>.Unspecified();
}

public sealed record UpdateEventEventTimeZoneDto
{
    public OptionalUpdate<string?> Value { get; init; } = OptionalUpdate<string?>.Unspecified();
}

public sealed record UpdateEventBackgroundColorDto
{
    public OptionalUpdate<string?> Value { get; init; } = OptionalUpdate<string?>.Unspecified();
}

public sealed record UpdateEventBackgroundEffectDto
{
    public OptionalUpdate<string?> Value { get; init; } = OptionalUpdate<string?>.Unspecified();
}

public sealed record UpdateEventBackgroundImageDto
{
    public OptionalUpdate<Guid?> Value { get; init; } = OptionalUpdate<Guid?>.Unspecified();
}

public sealed record UpdateEventSourceTemplateDto
{
    public OptionalUpdate<Guid?> Value { get; init; } = OptionalUpdate<Guid?>.Unspecified();
}

public sealed record UpdateEventSeriesMembershipDto
{
    public OptionalUpdate<Guid?> Value { get; init; } = OptionalUpdate<Guid?>.Unspecified();
}

public sealed record UpdateEventSeriesOrderDto
{
    public OptionalUpdate<int?> Value { get; init; } = OptionalUpdate<int?>.Unspecified();
}

public sealed record UpdateEventRegistrationPolicyDto
{
    public OptionalUpdate<int?> Value { get; init; } = OptionalUpdate<int?>.Unspecified();
}
