using Explore.Application.Models.Common;

namespace Explore.Application.DTOs.EventSessionAgendaItem;

public sealed record UpdateEventSessionAgendaItemDto
{
    public UpdateEventSessionAgendaItemRelationshipDto? Relationship { get; init; }
    public UpdateEventSessionAgendaItemContentDto? Content { get; init; }
    public UpdateEventSessionAgendaItemScheduleDto? Schedule { get; init; }
    public UpdateEventSessionAgendaItemLocationDto? Location { get; init; }
}

public sealed record UpdateEventSessionAgendaItemRelationshipDto
{
    public Guid EventSessionId { get; init; }
}

public sealed record UpdateEventSessionAgendaItemContentDto
{
    public string? Title { get; init; }
    public OptionalUpdate<string?> Description { get; init; } = OptionalUpdate<string?>.Unspecified();
}

public sealed record UpdateEventSessionAgendaItemScheduleDto
{
    public DateTimeOffset? StartTime { get; init; }
    public DateTimeOffset? EndTime { get; init; }
}

public sealed record UpdateEventSessionAgendaItemLocationDto
{
    public OptionalUpdate<Guid?> Value { get; init; } = OptionalUpdate<Guid?>.Unspecified();
}
