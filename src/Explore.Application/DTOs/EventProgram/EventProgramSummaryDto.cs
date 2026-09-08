using System.Collections.Immutable;
using Explore.Application.DTOs.Location;

namespace Explore.Application.DTOs.EventProgram;

public sealed record EventProgramSummaryDto
{
    private IReadOnlyList<EventProgramSectionDto>? _sections = ImmutableArray<EventProgramSectionDto>.Empty;
    private IReadOnlyList<EventProgramReadinessWarningDto>? _readinessWarnings = ImmutableArray<EventProgramReadinessWarningDto>.Empty;

    public Guid EventId { get; init; }
    public string EventTitle { get; init; } = string.Empty;
    public string? TimeZoneId { get; init; }
    public IReadOnlyList<EventProgramSectionDto> Sections { get => _sections!; init => _sections = value?.ToImmutableArray(); }
    public IReadOnlyList<EventProgramReadinessWarningDto> ReadinessWarnings { get => _readinessWarnings!; init => _readinessWarnings = value?.ToImmutableArray(); }
}

public sealed record EventProgramSectionDto
{
    private IReadOnlyList<EventProgramSessionGroupSectionDto>? _sessionGroups = ImmutableArray<EventProgramSessionGroupSectionDto>.Empty;

    public string SectionKey { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public IReadOnlyList<EventProgramSessionGroupSectionDto> SessionGroups { get => _sessionGroups!; init => _sessionGroups = value?.ToImmutableArray(); }
}

public sealed record EventProgramSessionGroupSectionDto
{
    private IReadOnlyList<EventProgramDayGroupDto>? _days = ImmutableArray<EventProgramDayGroupDto>.Empty;

    public Guid? SessionGroupId { get; init; }
    public string Title { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public string? Color { get; init; }
    public string? LocationName { get; init; }
    public string? RoomName { get; init; }
    public EventLocationPublicDto? EventLocation { get; init; }
    public IReadOnlyList<EventProgramDayGroupDto> Days { get => _days!; init => _days = value?.ToImmutableArray(); }
}

public sealed record EventProgramDayGroupDto
{
    private IReadOnlyList<EventProgramItemDto>? _items = ImmutableArray<EventProgramItemDto>.Empty;

    public DateOnly? LocalDate { get; init; }
    public string DisplayLabel { get; init; } = string.Empty;
    public IReadOnlyList<EventProgramItemDto> Items { get => _items!; init => _items = value?.ToImmutableArray(); }
}

public sealed record EventProgramItemDto
{
    private IReadOnlyList<EventProgramReadinessWarningDto>? _readinessWarnings = ImmutableArray<EventProgramReadinessWarningDto>.Empty;

    public Guid SessionId { get; init; }
    public string Title { get; init; } = string.Empty;
    public int? EventSessionKindId { get; init; }
    public string? EventSessionKindName { get; init; }
    public string? EventSessionKindMasterCode { get; init; }
    public DateTimeOffset? StartsAtUtc { get; init; }
    public DateTimeOffset? EndsAtUtc { get; init; }
    public DateOnly? LocalDate { get; init; }
    public TimeOnly? LocalStartTime { get; init; }
    public TimeOnly? LocalEndTime { get; init; }
    public int SortOrder { get; init; }
    public Guid? SessionGroupId { get; init; }
    public string? LocationName { get; init; }
    public string? RoomName { get; init; }
    public EventLocationPublicDto? EventLocation { get; init; }
    public int? Capacity { get; init; }
    public string? RegistrationModeName { get; init; }
    public IReadOnlyList<EventProgramReadinessWarningDto> ReadinessWarnings { get => _readinessWarnings!; init => _readinessWarnings = value?.ToImmutableArray(); }
}

public sealed record EventProgramReadinessWarningDto
{
    public string Path { get; init; } = string.Empty;
    public string Severity { get; init; } = "warning";
    public string Message { get; init; } = string.Empty;
}
