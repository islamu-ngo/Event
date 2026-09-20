using Explore.Application.DTOs.Madhab;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Both, AutoUserMappings = false)]
public static partial class MadhabMapper
{
    public static partial MadhabDto? ToDetail(Madhab? source);

    public static partial MadhabListDto ToListItem(Madhab source);
}
