using AutoMapper;
using Explore.Application.DTOs.FileType;

namespace Explore.Application.Profiles;

public class LookupMappingProfile : Profile
{
    public LookupMappingProfile()
    {
        CreateMap<Domain.FileType, FileTypeDto>().ReverseMap();
        CreateMap<Domain.FileType, FileTypeListDto>().ReverseMap();
    }

}
