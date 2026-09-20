using Explore.Application.DTOs.EventAgendaItem;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

public static partial class EventMapper
{
    // Physical placement is resolved by the disclosure service, not copied from the tracked graph.
    [MapperIgnoreSource(nameof(EventAgendaItem.EventDay))]
    [MapperIgnoreSource(nameof(EventAgendaItem.EventLocationId))]
    [MapperIgnoreSource(nameof(EventAgendaItem.EventLocation))]
    [MapperIgnoreSource(nameof(EventAgendaItem.LocationId))]
    [MapperIgnoreSource(nameof(EventAgendaItem.Location))]
    [MapperIgnoreSource(nameof(EventAgendaItem.RoomId))]
    [MapperIgnoreSource(nameof(EventAgendaItem.Room))]
    [MapperIgnoreSource(nameof(EventAgendaItem.Tenant))]
    [MapperIgnoreSource(nameof(EventAgendaItem.CreatedAt))]
    [MapperIgnoreSource(nameof(EventAgendaItem.CreatedBy))]
    [MapperIgnoreSource(nameof(EventAgendaItem.UpdatedAt))]
    [MapperIgnoreSource(nameof(EventAgendaItem.UpdatedBy))]
    [MapperIgnoreSource(nameof(EventAgendaItem.IsDeleted))]
    [MapperIgnoreSource(nameof(EventAgendaItem.DeletedAt))]
    [MapperIgnoreSource(nameof(EventAgendaItem.DeletedBy))]
    [MapperIgnoreTarget(nameof(EventAgendaItemDto.LocationId))]
    [MapperIgnoreTarget(nameof(EventAgendaItemDto.RoomId))]
    [MapperIgnoreTarget(nameof(EventAgendaItemDto.EventLocation))]
    [MapProperty(nameof(EventAgendaItem.Event), nameof(EventAgendaItemDto.EventTitle), Use = nameof(EventTitle))]
    [MapProperty(nameof(EventAgendaItem.Kind), nameof(EventAgendaItemDto.KindFullName), Use = nameof(AgendaKindName))]
    public static partial EventAgendaItemDto ToDetail(EventAgendaItem source);

    // List omits local end-date/minute caches and description as well as audit and placement graphs.
    [MapperIgnoreSource(nameof(EventAgendaItem.Event))]
    [MapperIgnoreSource(nameof(EventAgendaItem.EventDayId))]
    [MapperIgnoreSource(nameof(EventAgendaItem.EventDay))]
    [MapperIgnoreSource(nameof(EventAgendaItem.Description))]
    [MapperIgnoreSource(nameof(EventAgendaItem.LocalEndDate))]
    [MapperIgnoreSource(nameof(EventAgendaItem.LocalStartMinuteOfDay))]
    [MapperIgnoreSource(nameof(EventAgendaItem.LocalEndMinuteOfDay))]
    [MapperIgnoreSource(nameof(EventAgendaItem.EventLocationId))]
    [MapperIgnoreSource(nameof(EventAgendaItem.EventLocation))]
    [MapperIgnoreSource(nameof(EventAgendaItem.LocationId))]
    [MapperIgnoreSource(nameof(EventAgendaItem.Location))]
    [MapperIgnoreSource(nameof(EventAgendaItem.RoomId))]
    [MapperIgnoreSource(nameof(EventAgendaItem.Room))]
    [MapperIgnoreSource(nameof(EventAgendaItem.TenantId))]
    [MapperIgnoreSource(nameof(EventAgendaItem.Tenant))]
    [MapperIgnoreSource(nameof(EventAgendaItem.CreatedAt))]
    [MapperIgnoreSource(nameof(EventAgendaItem.CreatedBy))]
    [MapperIgnoreSource(nameof(EventAgendaItem.UpdatedAt))]
    [MapperIgnoreSource(nameof(EventAgendaItem.UpdatedBy))]
    [MapperIgnoreSource(nameof(EventAgendaItem.IsDeleted))]
    [MapperIgnoreSource(nameof(EventAgendaItem.DeletedAt))]
    [MapperIgnoreSource(nameof(EventAgendaItem.DeletedBy))]
    [MapperIgnoreTarget(nameof(EventAgendaItemListDto.EventLocation))]
    [MapProperty(nameof(EventAgendaItem.Kind), nameof(EventAgendaItemListDto.KindFullName), Use = nameof(AgendaKindName))]
    public static partial EventAgendaItemListDto ToListItem(EventAgendaItem source);

    private static string? AgendaKindName(ScheduleItemKind? source) => source?.FullName;
}
