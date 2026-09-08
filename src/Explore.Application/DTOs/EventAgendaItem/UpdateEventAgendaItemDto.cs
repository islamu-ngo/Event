namespace Explore.Application.DTOs.EventAgendaItem;

using Explore.Application.Models.Common;

public sealed record UpdateEventAgendaItemDto
{
    public UpdateEventAgendaItemEventDto? Event { get; init; }
    public UpdateEventAgendaItemTitleDto? Title { get; init; }
    public UpdateEventAgendaItemDescriptionDto? Description { get; init; }
    public UpdateEventAgendaItemScheduleDto? Schedule { get; init; }
    public UpdateEventAgendaItemLocationDto? Location { get; init; }
    public UpdateEventAgendaItemRoomDto? Room { get; init; }
    public UpdateEventAgendaItemKindDto? Kind { get; init; }
    public UpdateEventAgendaItemSortOrderDto? SortOrder { get; init; }
}

public sealed record UpdateEventAgendaItemEventDto
{
    public Guid EventId { get; init; }
}

public sealed record UpdateEventAgendaItemTitleDto
{
    public required string Value { get; init; }
}

public sealed record UpdateEventAgendaItemDescriptionDto
{
    public OptionalUpdate<string?> Value { get; init; } = OptionalUpdate<string?>.Unspecified();
}

public sealed record UpdateEventAgendaItemScheduleDto
{
    public DateTimeOffset StartTime { get; init; }
    public DateTimeOffset EndTime { get; init; }
}

public sealed record UpdateEventAgendaItemLocationDto
{
    public OptionalUpdate<Guid?> Value { get; init; } = OptionalUpdate<Guid?>.Unspecified();
}

public sealed record UpdateEventAgendaItemRoomDto
{
    public OptionalUpdate<Guid?> Value { get; init; } = OptionalUpdate<Guid?>.Unspecified();
}

public sealed record UpdateEventAgendaItemKindDto
{
    public OptionalUpdate<int?> Value { get; init; } = OptionalUpdate<int?>.Unspecified();
}

public sealed record UpdateEventAgendaItemSortOrderDto
{
    public int Value { get; init; }
}
