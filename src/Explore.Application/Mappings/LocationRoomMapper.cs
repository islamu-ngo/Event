using Explore.Application.DTOs.LocationRoom;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Both, AutoUserMappings = false)]
public static partial class LocationRoomMapper
{
    // Select the parent name only; never traverse its address, ownership or room graph.
    [MapperIgnoreSource(nameof(LocationRoom.Tenant))]
    [MapperIgnoreSource(nameof(LocationRoom.CreatedAt))]
    [MapperIgnoreSource(nameof(LocationRoom.CreatedBy))]
    [MapperIgnoreSource(nameof(LocationRoom.UpdatedAt))]
    [MapperIgnoreSource(nameof(LocationRoom.UpdatedBy))]
    [MapperIgnoreSource(nameof(LocationRoom.IsDeleted))]
    [MapperIgnoreSource(nameof(LocationRoom.DeletedAt))]
    [MapperIgnoreSource(nameof(LocationRoom.DeletedBy))]
    [MapProperty(nameof(LocationRoom.Location), nameof(LocationRoomDto.LocationFullName), Use = nameof(LocationName))]
    public static partial LocationRoomDto? ToDetail(LocationRoom? source);

    [MapperIgnoreSource(nameof(LocationRoom.Location))]
    [MapperIgnoreSource(nameof(LocationRoom.TenantId))]
    [MapperIgnoreSource(nameof(LocationRoom.Slug))]
    [MapperIgnoreSource(nameof(LocationRoom.Description))]
    [MapperIgnoreSource(nameof(LocationRoom.Tenant))]
    [MapperIgnoreSource(nameof(LocationRoom.CreatedAt))]
    [MapperIgnoreSource(nameof(LocationRoom.CreatedBy))]
    [MapperIgnoreSource(nameof(LocationRoom.UpdatedAt))]
    [MapperIgnoreSource(nameof(LocationRoom.UpdatedBy))]
    [MapperIgnoreSource(nameof(LocationRoom.IsDeleted))]
    [MapperIgnoreSource(nameof(LocationRoom.DeletedAt))]
    [MapperIgnoreSource(nameof(LocationRoom.DeletedBy))]
    public static partial LocationRoomListDto ToListItem(LocationRoom source);

    // The handler supplies the validated parent's tenant; persistence owns identity, audit and navigation state.
    public static LocationRoom Create(CreateLocationRoomDto source, Guid tenantId) => new()
    {
        LocationId = source.LocationId,
        Name = source.Name,
        Slug = source.Slug,
        Description = source.Description,
        Capacity = source.Capacity,
        SortOrder = source.SortOrder,
        TenantId = tenantId,
        Location = null!,
        Tenant = null!
    };

    private static string? LocationName(Location? location) => location?.FullName;
}
