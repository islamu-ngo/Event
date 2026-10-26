using System.Collections.Immutable;
using Explore.Application.DTOs.EventAgendaItem;
using Explore.Application.DTOs.EventDay;
using Explore.Application.DTOs.EventSession;
using Explore.Application.DTOs.EventSessionGroup;

namespace Explore.Application.DTOs.EventProgram;

public sealed record ManagedEventProgramDto(
    IReadOnlyList<EventSessionListDto> Sessions,
    IReadOnlyList<EventSessionGroupListDto> SessionGroups,
    IReadOnlyList<EventDayListDto> Days,
    IReadOnlyList<EventAgendaItemListDto> AgendaItems)
{
    private IReadOnlyList<EventSessionListDto> _sessions = Sessions.ToImmutableArray();
    private IReadOnlyList<EventSessionGroupListDto> _sessionGroups = SessionGroups.ToImmutableArray();
    private IReadOnlyList<EventDayListDto> _days = Days.ToImmutableArray();
    private IReadOnlyList<EventAgendaItemListDto> _agendaItems = AgendaItems.ToImmutableArray();

    public IReadOnlyList<EventSessionListDto> Sessions
    {
        get => _sessions;
        init => _sessions = value.ToImmutableArray();
    }

    public IReadOnlyList<EventSessionGroupListDto> SessionGroups
    {
        get => _sessionGroups;
        init => _sessionGroups = value.ToImmutableArray();
    }

    public IReadOnlyList<EventDayListDto> Days
    {
        get => _days;
        init => _days = value.ToImmutableArray();
    }

    public IReadOnlyList<EventAgendaItemListDto> AgendaItems
    {
        get => _agendaItems;
        init => _agendaItems = value.ToImmutableArray();
    }
}
