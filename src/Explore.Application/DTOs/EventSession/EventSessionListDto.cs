using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Explore.Application.DTOs.Location;
using Explore.Domain.Enums;
namespace Explore.Application.DTOs.EventSession;

public sealed record EventSessionListDto
{
    public Guid Id { get; init; }
    public Guid ConcurrencyStamp { get; init; }

    // Event relationship
    public Guid EventId { get; init; }
    public required string EventTitle { get; init; }
    public int ParentEventStatusId { get; init; }

    // Day assignment
    public Guid? EventDayId { get; init; }

    // Timing (UTC)
    public DateTimeOffset? StartTime { get; init; }
    public DateTimeOffset? EndTime { get; init; }
    public SessionEndTimeType EndTimeType { get; init; }
    public string? FormattedEndTime { get; init; }
    public bool IsScheduled { get; init; }

    // Cached local projections (event timezone)
    public DateOnly? LocalStartDate { get; init; }
    public TimeOnly? LocalStartTime { get; init; }
    public TimeOnly? LocalEndTime { get; init; }

    // Ordering
    public int SortOrder { get; init; }

    // Location
    public Guid? LocationId { get; set; }
    public string? LocationFullName { get; set; }
    public string? LocationCity { get; set; }

    // Room
    public Guid? RoomId { get; set; }
    public string? RoomName { get; set; }
    public EventLocationPublicDto? EventLocation { get; set; }

    // Session Details
    public string? Title { get; init; }
    public int? EventSessionKindId { get; init; }
    public string? EventSessionKindFullName { get; init; }
    public string? EventSessionKindMasterCode { get; init; }
    public int EventSessionStatusId { get; init; }
    public string? EventSessionStatusFullName { get; init; }
    public string? EventSessionStatusMasterCode { get; init; }
    public string? Slug { get; init; }

    // Media
    public Guid? FeaturedImageId { get; init; }
    public string? FeaturedImageUri { get; init; }

    // Attendance
    public int? MaxAudienceAttendees { get; init; }
    public int? CurrentAudienceAttendees { get; init; }

    // Registration
    public int? RegistrationModeId { get; init; }
    public string? RegistrationModeFullName { get; init; }

    // Optional Islamic extension for this session
    public EventSessionIslamicAspectDto? IslamicAspect { get; init; }

    // Program sections/tracks/devrooms this session belongs to
    private IReadOnlyList<EventSessionGroupAssignmentDto>? _sessionGroups = ImmutableArray<EventSessionGroupAssignmentDto>.Empty;

    public IReadOnlyList<EventSessionGroupAssignmentDto> SessionGroups
    {
        get => _sessionGroups!;
        init => _sessionGroups = value?.ToImmutableArray();
    }

    public Guid TenantId { get; init; }
}
