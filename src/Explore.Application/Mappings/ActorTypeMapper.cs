using Explore.Application.DTOs.ActorType;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Both, AutoUserMappings = false)]
public static partial class ActorTypeMapper
{
    public static partial ActorTypeDto? ToDetail(ActorType? source);

    public static partial ActorTypeListDto ToListItem(ActorType source);
}
