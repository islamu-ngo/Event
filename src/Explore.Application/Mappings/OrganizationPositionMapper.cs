using Explore.Application.DTOs.OrganizationPosition;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Both, AutoUserMappings = false)]
public static partial class OrganizationPositionMapper
{
    public static partial OrganizationPositionDto? ToDetail(OrganizationPosition? source);

    public static partial OrganizationPositionListDto ToListItem(OrganizationPosition source);
}
