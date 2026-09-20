using Explore.Application.DTOs.EventFormat;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Both, AutoUserMappings = false)]
public static partial class EventFormatMapper
{
    public static partial EventFormatDto? ToDetail(EventFormat? source);

    public static partial EventFormatListDto ToListItem(EventFormat source);
}
