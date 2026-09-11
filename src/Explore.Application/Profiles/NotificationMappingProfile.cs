using AutoMapper;
using Explore.Domain;

namespace Explore.Application.Profiles;

public class NotificationMappingProfile : Profile
{
    public NotificationMappingProfile()
    {
        CreateMap<CustomPropertyProjectionStatus, DTOs.CustomPropertyProjection.ProjectionStatusDto>();
        CreateMap<CustomPropertyProjectionDirtyScope, DTOs.CustomPropertyProjection.ProjectionDirtyScopeDto>();
        CreateMap<EventCustomPropertyProjection, DTOs.CustomPropertyProjection.EventCustomPropertyProjectionDto>();
        CreateMap<EventSessionCustomPropertyProjection, DTOs.CustomPropertyProjection.EventSessionCustomPropertyProjectionDto>();
    }
}
