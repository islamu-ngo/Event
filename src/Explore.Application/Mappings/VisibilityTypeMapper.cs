using Explore.Application.DTOs.VisibilityType;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Both, AutoUserMappings = false)]
public static partial class VisibilityTypeMapper
{
    public static partial VisibilityTypeDto? ToDetail(VisibilityType? source);

    public static partial VisibilityTypeListDto ToListItem(VisibilityType source);
}
