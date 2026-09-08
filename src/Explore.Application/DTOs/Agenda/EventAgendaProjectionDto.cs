namespace Explore.Application.DTOs.Agenda;

using System.Collections.Immutable;

public sealed record EventAgendaProjectionDto
{
    public Guid EventId { get; init; }
    public string? EventTitle { get; init; }
    public string? Timezone { get; init; }

    private IReadOnlyList<AgendaDayGroupDto>? _days = ImmutableArray<AgendaDayGroupDto>.Empty;

    public IReadOnlyList<AgendaDayGroupDto> Days
    {
        get => _days!;
        init => _days = value?.ToImmutableArray();
    }
}
