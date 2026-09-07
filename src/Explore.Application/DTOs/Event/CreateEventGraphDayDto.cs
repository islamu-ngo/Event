using System;

namespace Explore.Application.DTOs.Event;

public sealed record CreateEventGraphDayDto
{
    public string? TempKey { get; init; }
    public DateOnly LocalDate { get; init; }
    public string? Label { get; init; }
    public string? Description { get; init; }
    public string? BannerText { get; init; }
    public Guid? BannerImageId { get; init; }
    public bool IsPublished { get; init; } = true;
    public int SortOrder { get; init; }
    public bool AllowsDayScopeRegistration { get; init; }
}
