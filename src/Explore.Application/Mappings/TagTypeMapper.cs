using Explore.Application.DTOs.TagType;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Both, AutoUserMappings = false)]
public static partial class TagTypeMapper
{
    public static partial TagTypeDto? ToDetail(TagType? source);

    [MapperIgnoreSource(nameof(TagType.Description))]
    public static partial TagTypeListDto ToListItem(TagType source);
}
