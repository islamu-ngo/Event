using Explore.Application.Models.Common;

namespace Explore.Application.DTOs.EventDay;

public sealed record UpdateEventDayDto
{
    public UpdateEventDayEventDto? Event { get; init; }
    public UpdateEventDayLocalDateDto? LocalDate { get; init; }
    public UpdateEventDayLabelDto? Label { get; init; }
    public UpdateEventDayDescriptionDto? Description { get; init; }
    public UpdateEventDayBannerTextDto? BannerText { get; init; }
    public UpdateEventDayBannerImageDto? BannerImage { get; init; }
    public UpdateEventDayPublicationDto? Publication { get; init; }
    public UpdateEventDaySortOrderDto? SortOrder { get; init; }
    public UpdateEventDayRegistrationDto? Registration { get; init; }
}

public sealed record UpdateEventDayEventDto
{
    public Guid EventId { get; init; }
}

public sealed record UpdateEventDayLocalDateDto
{
    public DateOnly Value { get; init; }
}

public sealed record UpdateEventDayLabelDto
{
    public OptionalUpdate<string?> Value { get; init; } = OptionalUpdate<string?>.Unspecified();
}

public sealed record UpdateEventDayDescriptionDto
{
    public OptionalUpdate<string?> Value { get; init; } = OptionalUpdate<string?>.Unspecified();
}

public sealed record UpdateEventDayBannerTextDto
{
    public OptionalUpdate<string?> Value { get; init; } = OptionalUpdate<string?>.Unspecified();
}

public sealed record UpdateEventDayBannerImageDto
{
    public OptionalUpdate<Guid?> Value { get; init; } = OptionalUpdate<Guid?>.Unspecified();
}

public sealed record UpdateEventDayPublicationDto
{
    public bool IsPublished { get; init; }
}

public sealed record UpdateEventDaySortOrderDto
{
    public int Value { get; init; }
}

public sealed record UpdateEventDayRegistrationDto
{
    public bool AllowsDayScopeRegistration { get; init; }
}
