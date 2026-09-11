using Explore.Application.DTOs.Location;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Both, AutoUserMappings = false)]
public static partial class LocationMapper
{
    // This existing management projection does not decide address disclosure authority.
    // PII is consumed only through address wrappers and validated coordinate extraction.
    [MapperIgnoreSource(nameof(Location.DisplaySortKey))]
    [MapperIgnoreSource(nameof(Location.DisplaySortKeyVersion))]
    [MapperIgnoreSource(nameof(Location.Tenant))]
    [MapperIgnoreSource(nameof(Location.LocationKind))]
    [MapperIgnoreSource(nameof(Location.LocationPrivacyStateId))]
    [MapperIgnoreSource(nameof(Location.LocationPrivacyState))]
    [MapperIgnoreSource(nameof(Location.AddressSourceId))]
    [MapperIgnoreSource(nameof(Location.AddressSource))]
    [MapperIgnoreSource(nameof(Location.AddressSourceLookup))]
    [MapperIgnoreSource(nameof(Location.AddressVisibilityId))]
    [MapperIgnoreSource(nameof(Location.AddressVisibility))]
    [MapperIgnoreSource(nameof(Location.AddressVisibilityLookup))]
    [MapperIgnoreSource(nameof(Location.AddressOrganizationId))]
    [MapperIgnoreSource(nameof(Location.AddressOrganizationTenant))]
    [MapperIgnoreSource(nameof(Location.OwnerUserId))]
    [MapperIgnoreSource(nameof(Location.OwnerUser))]
    [MapperIgnoreSource(nameof(Location.PiiErasedAtUtc))]
    [MapperIgnoreSource(nameof(Location.PiiErasureReason))]
    [MapperIgnoreSource(nameof(Location.Rooms))]
    [MapperIgnoreSource(nameof(Location.CreatedAt))]
    [MapperIgnoreSource(nameof(Location.CreatedBy))]
    [MapperIgnoreSource(nameof(Location.UpdatedAt))]
    [MapperIgnoreSource(nameof(Location.UpdatedBy))]
    [MapProperty(nameof(Location.Address), nameof(LocationDto.Address), Use = nameof(PreserveOptionalText))]
    [MapProperty(nameof(Location.Postcode), nameof(LocationDto.Postcode), Use = nameof(PreserveOptionalText))]
    [MapProperty(nameof(Location.Pii), nameof(LocationDto.Latitude), Use = nameof(Latitude))]
    [MapProperty(nameof(Location.Pii), nameof(LocationDto.Longitude), Use = nameof(Longitude))]
    public static partial LocationDto? ToDetail(Location? source);

    [MapperIgnoreSource(nameof(Location.Postcode))]
    [MapperIgnoreSource(nameof(Location.Pii))]
    [MapperIgnoreSource(nameof(Location.LocationKindId))]
    [MapperIgnoreSource(nameof(Location.DisplaySortKey))]
    [MapperIgnoreSource(nameof(Location.DisplaySortKeyVersion))]
    [MapperIgnoreSource(nameof(Location.Tenant))]
    [MapperIgnoreSource(nameof(Location.LocationKind))]
    [MapperIgnoreSource(nameof(Location.LocationPrivacyStateId))]
    [MapperIgnoreSource(nameof(Location.LocationPrivacyState))]
    [MapperIgnoreSource(nameof(Location.AddressSourceId))]
    [MapperIgnoreSource(nameof(Location.AddressSource))]
    [MapperIgnoreSource(nameof(Location.AddressSourceLookup))]
    [MapperIgnoreSource(nameof(Location.AddressVisibilityId))]
    [MapperIgnoreSource(nameof(Location.AddressVisibility))]
    [MapperIgnoreSource(nameof(Location.AddressVisibilityLookup))]
    [MapperIgnoreSource(nameof(Location.AddressOrganizationId))]
    [MapperIgnoreSource(nameof(Location.AddressOrganizationTenant))]
    [MapperIgnoreSource(nameof(Location.OwnerUserId))]
    [MapperIgnoreSource(nameof(Location.OwnerUser))]
    [MapperIgnoreSource(nameof(Location.PiiErasedAtUtc))]
    [MapperIgnoreSource(nameof(Location.PiiErasureReason))]
    [MapperIgnoreSource(nameof(Location.Rooms))]
    [MapperIgnoreSource(nameof(Location.CreatedAt))]
    [MapperIgnoreSource(nameof(Location.CreatedBy))]
    [MapperIgnoreSource(nameof(Location.UpdatedAt))]
    [MapperIgnoreSource(nameof(Location.UpdatedBy))]
    [MapProperty(nameof(Location.Address), nameof(LocationListDto.Address), Use = nameof(PreserveOptionalText))]
    public static partial LocationListDto ToListItem(Location source);

    // Existing required DTO strings serialize null when PII is absent or erased, not empty text.
    private static string PreserveOptionalText(string? value) => value!;
    private static double? Latitude(LocationPii? pii) => pii?.GetCoordinate()?.Latitude;
    private static double? Longitude(LocationPii? pii) => pii?.GetCoordinate()?.Longitude;
}
