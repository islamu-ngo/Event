using Explore.Application.DTOs.Language;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Both, AutoUserMappings = false)]
public static partial class LanguageMapper
{
    public static partial LanguageDto? ToDetail(Language? source);

    public static partial LanguageListDto ToListItem(Language source);
}
