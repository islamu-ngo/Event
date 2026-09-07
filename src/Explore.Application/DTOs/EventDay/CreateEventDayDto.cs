namespace Explore.Application.DTOs.EventDay;

public sealed record CreateEventDayDto
{
    public Guid EventId { get; init; }
    public DateOnly LocalDate { get; init; }
    public string? Label { get; init; }
    public string? Description { get; init; }
    public string? BannerText { get; init; }
    public Guid? BannerImageId { get; init; }
    public bool IsPublished { get; init; }
    public int SortOrder { get; init; }
    public bool AllowsDayScopeRegistration { get; init; }
}
