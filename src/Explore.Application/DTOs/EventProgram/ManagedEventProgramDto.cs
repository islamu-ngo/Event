using Explore.Application.DTOs.EventAgendaItem;
using Explore.Application.DTOs.EventDay;
using Explore.Application.DTOs.EventSession;
using Explore.Application.DTOs.EventSessionGroup;

namespace Explore.Application.DTOs.EventProgram;

public sealed record ManagedEventProgramDto(
    IReadOnlyList<EventSessionListDto> Sessions,
    IReadOnlyList<EventSessionGroupListDto> SessionGroups,
    IReadOnlyList<EventDayListDto> Days,
    IReadOnlyList<EventAgendaItemListDto> AgendaItems);
