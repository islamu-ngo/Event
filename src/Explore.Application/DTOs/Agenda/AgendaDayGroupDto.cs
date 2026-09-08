namespace Explore.Application.DTOs.Agenda;

using System.Collections.Immutable;

public sealed record AgendaDayGroupDto
{
    public Guid? EventDayId { get; init; }
    public DateOnly LocalDate { get; init; }
    public string? Label { get; init; }
    public string? Description { get; init; }
    public bool IsPublished { get; init; }
    public int SortOrder { get; init; }
    public bool AllowsDayScopeRegistration { get; init; }

    private IReadOnlyList<AgendaScheduleEntryDto>? _entries = ImmutableArray<AgendaScheduleEntryDto>.Empty;

    public IReadOnlyList<AgendaScheduleEntryDto> Entries
    {
        get => _entries!;
        init => _entries = value?.ToImmutableArray();
    }
}
