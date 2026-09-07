using Explore.Application.Models.Common;
using Explore.Domain.Enums;

namespace Explore.Application.DTOs.EventSession;

public sealed record UpdateEventSessionDto
{
    public UpdateEventSessionEventDto? Event { get; init; }
    public UpdateEventSessionScheduleDto? Schedule { get; init; }
    public UpdateEventSessionLocationDto? Location { get; init; }
    public UpdateEventSessionFeaturedImageDto? FeaturedImage { get; init; }
    public UpdateEventSessionRoomDto? Room { get; init; }
    public UpdateEventSessionSortOrderDto? SortOrder { get; init; }
    public UpdateEventSessionTitleDto? Title { get; init; }
    public UpdateEventSessionKindDto? Kind { get; init; }
    public UpdateEventSessionDescriptionDto? Description { get; init; }
    public UpdateEventSessionSlugDto? Slug { get; init; }
    public UpdateEventSessionMaxAudienceAttendeesDto? MaxAudienceAttendees { get; init; }
    public UpdateEventSessionRegistrationModeDto? RegistrationMode { get; init; }
    public UpdateEventSessionIslamicAspectUpdateDto? IslamicAspect { get; init; }
}

public sealed record UpdateEventSessionEventDto
{
    public Guid EventId { get; init; }
}

public sealed record UpdateEventSessionScheduleDto
{
    public OptionalUpdate<DateTimeOffset?> StartTime { get; init; } = OptionalUpdate<DateTimeOffset?>.Unspecified();
    public OptionalUpdate<DateTimeOffset?> EndTime { get; init; } = OptionalUpdate<DateTimeOffset?>.Unspecified();
    public OptionalUpdate<SessionEndTimeType> EndTimeType { get; init; } = OptionalUpdate<SessionEndTimeType>.Unspecified();
}

public sealed record UpdateEventSessionLocationDto
{
    public OptionalUpdate<Guid?> Value { get; init; } = OptionalUpdate<Guid?>.Unspecified();
}

public sealed record UpdateEventSessionFeaturedImageDto
{
    public OptionalUpdate<Guid?> Value { get; init; } = OptionalUpdate<Guid?>.Unspecified();
}

public sealed record UpdateEventSessionRoomDto
{
    public OptionalUpdate<Guid?> Value { get; init; } = OptionalUpdate<Guid?>.Unspecified();
}

public sealed record UpdateEventSessionSortOrderDto
{
    public int Value { get; init; }
}

public sealed record UpdateEventSessionTitleDto
{
    public OptionalUpdate<string?> Value { get; init; } = OptionalUpdate<string?>.Unspecified();
}

public sealed record UpdateEventSessionKindDto
{
    public OptionalUpdate<int?> Value { get; init; } = OptionalUpdate<int?>.Unspecified();
}

public sealed record UpdateEventSessionDescriptionDto
{
    public OptionalUpdate<string?> Value { get; init; } = OptionalUpdate<string?>.Unspecified();
}

public sealed record UpdateEventSessionSlugDto
{
    public OptionalUpdate<string?> Value { get; init; } = OptionalUpdate<string?>.Unspecified();
}

public sealed record UpdateEventSessionMaxAudienceAttendeesDto
{
    public OptionalUpdate<int?> Value { get; init; } = OptionalUpdate<int?>.Unspecified();
}

public sealed record UpdateEventSessionRegistrationModeDto
{
    public OptionalUpdate<int?> Value { get; init; } = OptionalUpdate<int?>.Unspecified();
}

public sealed record UpdateEventSessionIslamicAspectUpdateDto
{
    public OptionalUpdate<EventSessionIslamicAspectDto?> Value { get; init; } = OptionalUpdate<EventSessionIslamicAspectDto?>.Unspecified();
}
