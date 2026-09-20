using Explore.Application.DTOs.EventDay;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

public static partial class EventMapper
{
    // Day responses omit audit, tenant/image graphs; only detail discloses presentation content.
    [MapperIgnoreSource(nameof(EventDay.Tenant))]
    [MapperIgnoreSource(nameof(EventDay.BannerImage))]
    [MapperIgnoreSource(nameof(EventDay.CreatedAt))]
    [MapperIgnoreSource(nameof(EventDay.CreatedBy))]
    [MapperIgnoreSource(nameof(EventDay.UpdatedAt))]
    [MapperIgnoreSource(nameof(EventDay.UpdatedBy))]
    [MapperIgnoreSource(nameof(EventDay.IsDeleted))]
    [MapperIgnoreSource(nameof(EventDay.DeletedAt))]
    [MapperIgnoreSource(nameof(EventDay.DeletedBy))]
    [MapProperty(nameof(EventDay.Event), nameof(EventDayDto.EventTitle), Use = nameof(EventTitle))]
    public static partial EventDayDto ToDetail(EventDay source);

    [MapperIgnoreSource(nameof(EventDay.Event))]
    [MapperIgnoreSource(nameof(EventDay.TenantId))]
    [MapperIgnoreSource(nameof(EventDay.Tenant))]
    [MapperIgnoreSource(nameof(EventDay.Description))]
    [MapperIgnoreSource(nameof(EventDay.BannerText))]
    [MapperIgnoreSource(nameof(EventDay.BannerImageId))]
    [MapperIgnoreSource(nameof(EventDay.BannerImage))]
    [MapperIgnoreSource(nameof(EventDay.CreatedAt))]
    [MapperIgnoreSource(nameof(EventDay.CreatedBy))]
    [MapperIgnoreSource(nameof(EventDay.UpdatedAt))]
    [MapperIgnoreSource(nameof(EventDay.UpdatedBy))]
    [MapperIgnoreSource(nameof(EventDay.IsDeleted))]
    [MapperIgnoreSource(nameof(EventDay.DeletedAt))]
    [MapperIgnoreSource(nameof(EventDay.DeletedBy))]
    public static partial EventDayListDto ToListItem(EventDay source);
}
