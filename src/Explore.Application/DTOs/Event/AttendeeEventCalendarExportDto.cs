namespace Explore.Application.DTOs.Event;

public sealed record AttendeeEventCalendarExportDto(
    Guid EventId,
    string Title,
    string? Description,
    string? Slug,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    string? Location);
