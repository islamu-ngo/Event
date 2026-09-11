using Explore.Application.DTOs.GroupPosition;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Both, AutoUserMappings = false)]
public static partial class GroupPositionMapper
{
    public static partial GroupPositionDto? ToDetail(GroupPosition? source);

    public static partial GroupPositionListDto ToListItem(GroupPosition source);
}
