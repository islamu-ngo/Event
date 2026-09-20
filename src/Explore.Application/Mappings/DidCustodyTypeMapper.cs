using Explore.Application.DTOs.DidCustodyType;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Both, AutoUserMappings = false)]
public static partial class DidCustodyTypeMapper
{
    public static partial DidCustodyTypeDto? ToDetail(DidCustodyType? source);

    public static partial DidCustodyTypeListDto ToListItem(DidCustodyType source);
}
