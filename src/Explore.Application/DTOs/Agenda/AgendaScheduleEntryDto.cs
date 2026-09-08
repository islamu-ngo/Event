using Explore.Application.DTOs.Location;

namespace Explore.Application.DTOs.Agenda;

public sealed record AgendaScheduleEntryDto
{
    public Guid Id { get; init; }

    /// <summary>
    /// Discriminator: "Session" or "AgendaItem".
    /// </summary>
    public required string EntryType { get; init; }

    public required string Title { get; init; }
    public string? Description { get; init; }

    public DateTimeOffset StartTime { get; init; }
    public DateTimeOffset EndTime { get; init; }

    public DateOnly LocalStartDate { get; init; }
    public TimeOnly LocalStartTime { get; init; }
    public TimeOnly LocalEndTime { get; init; }
    public int LocalStartMinuteOfDay { get; init; }
    public int LocalEndMinuteOfDay { get; init; }

    public Guid? RoomId { get; init; }
    public Guid? LocationId { get; init; }
    public EventLocationPublicDto? EventLocation { get; init; }

    // Session-specific fields (null for agenda items)
    public int? MaxAudienceAttendees { get; init; }
    public int? CurrentAudienceAttendees { get; init; }
    public int? RegistrationModeId { get; init; }
    public string? RegistrationModeFullName { get; init; }

    // AgendaItem-specific fields (null for sessions)
    public int? KindId { get; init; }
    public string? KindFullName { get; init; }

    public int SortOrder { get; init; }
}
