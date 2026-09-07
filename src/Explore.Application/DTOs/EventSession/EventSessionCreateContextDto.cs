namespace Explore.Application.DTOs.EventSession;

using System.Collections.Immutable;

public sealed record EventSessionCreateContextDto
{
    public Guid EventId { get; init; }
    public string EventTitle { get; init; } = string.Empty;
    public Guid TenantId { get; init; }
    public string? TimeZoneId { get; init; }
    public DateOnly? EventStartDate { get; init; }
    public DateOnly? EventEndDate { get; init; }
    public EventSessionCreateDefaultsDto Defaults { get; init; } = new();
    private IReadOnlyList<EventSessionCreateLocationOptionDto>? _locations = ImmutableArray<EventSessionCreateLocationOptionDto>.Empty;
    private IReadOnlyList<EventSessionCreateRoomOptionDto>? _rooms = ImmutableArray<EventSessionCreateRoomOptionDto>.Empty;
    private IReadOnlyList<EventSessionCreateGroupOptionDto>? _sessionGroups = ImmutableArray<EventSessionCreateGroupOptionDto>.Empty;
    private IReadOnlyList<string>? _notices = ImmutableArray<string>.Empty;

    public IReadOnlyList<EventSessionCreateLocationOptionDto> Locations { get => _locations!; init => _locations = value?.ToImmutableArray(); }
    public IReadOnlyList<EventSessionCreateRoomOptionDto> Rooms { get => _rooms!; init => _rooms = value?.ToImmutableArray(); }
    public IReadOnlyList<EventSessionCreateGroupOptionDto> SessionGroups { get => _sessionGroups!; init => _sessionGroups = value?.ToImmutableArray(); }
    public IReadOnlyList<string> Notices { get => _notices!; init => _notices = value?.ToImmutableArray(); }
}

public sealed record EventSessionCreateDefaultsDto
{
    public DateOnly? SessionDate { get; init; }
    public TimeSpan? StartTime { get; init; }
    public TimeSpan? EndTime { get; init; }
    public int RegistrationModeId { get; init; }
}

public sealed record EventSessionCreateLocationOptionDto
{
    public Guid Id { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty;
    public string? TimeZoneId { get; init; }
}

public sealed record EventSessionCreateRoomOptionDto
{
    public Guid Id { get; init; }
    public Guid LocationId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int? Capacity { get; init; }
    public int SortOrder { get; init; }
}

public sealed record EventSessionCreateGroupOptionDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public Guid? LocationId { get; init; }
    public string? LocationName { get; init; }
    public Guid? RoomId { get; init; }
    public string? RoomName { get; init; }
    public string? Color { get; init; }
    public int SortOrder { get; init; }
}
