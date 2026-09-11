using AutoMapper;
using Explore.Application.DTOs.FileType;
using Explore.Application.DTOs.TagTypeTags;

namespace Explore.Application.Profiles;

public class LookupMappingProfile : Profile
{
    public LookupMappingProfile()
    {
        CreateMap<Domain.TagTypeTags, TagTypeTagsDto>()
            .ForMember(dest => dest.TagFullName, opt => opt.MapFrom(src => src.Tag != null ? src.Tag.FullName : null))
            .ForMember(dest => dest.TagMasterCode, opt => opt.MapFrom(src => src.Tag != null ? src.Tag.MasterCode : null))
            .ForMember(dest => dest.TagTypeFullName, opt => opt.MapFrom(src => src.TagType != null ? src.TagType.FullName : null))
            .ForMember(dest => dest.TagTypeMasterCode, opt => opt.MapFrom(src => src.TagType != null ? src.TagType.MasterCode : null));
        CreateMap<Domain.TagTypeTags, TagTypeTagsListDto>()
            .ForMember(dest => dest.TagFullName, opt => opt.MapFrom(src => src.Tag != null ? src.Tag.FullName : null))
            .ForMember(dest => dest.TagMasterCode, opt => opt.MapFrom(src => src.Tag != null ? src.Tag.MasterCode : null))
            .ForMember(dest => dest.TagTypeFullName, opt => opt.MapFrom(src => src.TagType != null ? src.TagType.FullName : null))
            .ForMember(dest => dest.TagTypeMasterCode, opt => opt.MapFrom(src => src.TagType != null ? src.TagType.MasterCode : null));
        CreateMap<CreateTagTypeTagsDto, Domain.TagTypeTags>();
        CreateMap<UpdateTagTypeTagsDto, Domain.TagTypeTags>();

        CreateMap<Domain.FileType, FileTypeDto>().ReverseMap();
        CreateMap<Domain.FileType, FileTypeListDto>().ReverseMap();
    }

}
