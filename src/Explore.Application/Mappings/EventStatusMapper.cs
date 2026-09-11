using Explore.Application.DTOs.EventStatus;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Both, AutoUserMappings = false)]
public static partial class EventStatusMapper
{
    public static partial EventStatusDto? ToDetail(EventStatus? source);

    public static partial EventStatusListDto ToListItem(EventStatus source);
}
