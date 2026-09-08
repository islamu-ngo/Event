using AutoMapper;
using Explore.Application.DTOs.EventRegistrationPolicy;
using Explore.Application.DTOs.EventSessionKind;
using Explore.Application.DTOs.RegistrationMode;
using Explore.Application.DTOs.RegistrationScope;
using Explore.Application.DTOs.ScheduleItemKind;
using Explore.Domain;

namespace Explore.Application.Profiles;

public class RegistrationMappingProfile : Profile
{
    public RegistrationMappingProfile()
    {
        CreateMap<RegistrationScope, RegistrationScopeDto>().ReverseMap();
        CreateMap<RegistrationScope, RegistrationScopeListDto>().ReverseMap();

        CreateMap<EventRegistrationPolicy, EventRegistrationPolicyDto>().ReverseMap();
        CreateMap<EventRegistrationPolicy, EventRegistrationPolicyListDto>().ReverseMap();

        CreateMap<EventSessionKind, EventSessionKindDto>().ReverseMap();
        CreateMap<EventSessionKind, EventSessionKindListDto>().ReverseMap();

        CreateMap<ScheduleItemKind, ScheduleItemKindDto>().ReverseMap();
        CreateMap<ScheduleItemKind, ScheduleItemKindListDto>().ReverseMap();

        CreateMap<RegistrationMode, RegistrationModeDto>().ReverseMap();
        CreateMap<RegistrationMode, RegistrationModeListDto>().ReverseMap();
    }
}
