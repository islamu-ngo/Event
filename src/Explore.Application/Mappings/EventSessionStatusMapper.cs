using Explore.Application.DTOs.EventSessionStatus;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Both, AutoUserMappings = false)]
public static partial class EventSessionStatusMapper
{
    public static partial EventSessionStatusDto? ToDetail(EventSessionStatus? source);

    public static partial EventSessionStatusListDto ToListItem(EventSessionStatus source);
}
