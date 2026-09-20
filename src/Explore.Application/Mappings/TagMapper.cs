using Explore.Application.DTOs.Tag;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Both, AutoUserMappings = false)]
public static partial class TagMapper
{
    [MapperIgnoreSource(nameof(Tag.Tenant))]
    public static partial TagDto? ToDetail(Tag? source);

    [MapperIgnoreSource(nameof(Tag.TenantId))]
    [MapperIgnoreSource(nameof(Tag.Tenant))]
    public static partial TagListDto ToListItem(Tag source);

    // The handler supplies tenant authority; persistence owns the new identity and navigation.
    public static Tag Create(CreateTagDto source, Guid tenantId) => new()
    {
        MasterCode = source.MasterCode,
        FullName = source.FullName,
        TenantId = tenantId,
        Tenant = null!
    };
}
