using Explore.Application.DTOs.AudienceAge;
using Explore.Application.DTOs.AudienceGender;
using Explore.Application.DTOs.EventType;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

// Read projections only. Handlers retain mutation, authority and disclosure decisions.
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Both, AutoUserMappings = false)]
public static partial class EventMapper
{
    public static partial AudienceAgeDto ToDetail(AudienceAge source);
    public static partial AudienceAgeListDto ToListItem(AudienceAge source);
    public static partial AudienceGenderDto ToDetail(AudienceGender source);
    public static partial AudienceGenderListDto ToListItem(AudienceGender source);

    // Tenant navigation is not part of the public event-type catalog.
    [MapperIgnoreSource(nameof(EventType.TenantId))]
    [MapperIgnoreSource(nameof(EventType.Tenant))]
    public static partial EventTypeListDto ToListItem(EventType source);
}
