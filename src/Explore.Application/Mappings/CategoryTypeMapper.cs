using Explore.Application.DTOs.CategoryType;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Both, AutoUserMappings = false)]
public static partial class CategoryTypeMapper
{
    public static partial CategoryTypeDto? ToDetail(CategoryType? source);

    [MapperIgnoreSource(nameof(CategoryType.Description))]
    public static partial CategoryTypeListDto ToListItem(CategoryType source);
}
