using AutoMapper;
using Explore.Application.DTOs.CategoryTypeCategories;
using Explore.Application.DTOs.FileType;
using Explore.Application.DTOs.LocationRoom;
using Explore.Application.DTOs.Tag;
using Explore.Application.DTOs.TagTypeTags;
using Explore.Domain;

namespace Explore.Application.Profiles;

public class LookupMappingProfile : Profile
{
    public LookupMappingProfile()
    {
        CreateMap<LocationRoom, LocationRoomDto>()
            .ForMember(dest => dest.LocationFullName, opt => opt.MapFrom(src => src.Location != null ? src.Location.FullName : null));
        CreateMap<LocationRoom, LocationRoomListDto>();
        CreateMap<CreateLocationRoomDto, LocationRoom>();

        CreateMap<Tag, TagDto>().ReverseMap();
        CreateMap<Tag, TagListDto>();
        CreateMap<CreateTagDto, Tag>();

        CreateMap<Domain.CategoryTypeCategories, CategoryTypeCategoriesDto>()
            .ForMember(dest => dest.CategoryFullName, opt => opt.MapFrom(src => src.Category != null ? src.Category.FullName : null))
            .ForMember(dest => dest.CategoryMasterCode, opt => opt.MapFrom(src => src.Category != null ? src.Category.MasterCode : null))
            .ForMember(dest => dest.CategoryTypeFullName, opt => opt.MapFrom(src => src.CategoryType != null ? src.CategoryType.FullName : null))
            .ForMember(dest => dest.CategoryTypeMasterCode, opt => opt.MapFrom(src => src.CategoryType != null ? src.CategoryType.MasterCode : null));
        CreateMap<Domain.CategoryTypeCategories, CategoryTypeCategoriesListDto>()
            .ForMember(dest => dest.CategoryFullName, opt => opt.MapFrom(src => src.Category != null ? src.Category.FullName : null))
            .ForMember(dest => dest.CategoryMasterCode, opt => opt.MapFrom(src => src.Category != null ? src.Category.MasterCode : null))
            .ForMember(dest => dest.CategoryTypeFullName, opt => opt.MapFrom(src => src.CategoryType != null ? src.CategoryType.FullName : null))
            .ForMember(dest => dest.CategoryTypeMasterCode, opt => opt.MapFrom(src => src.CategoryType != null ? src.CategoryType.MasterCode : null));
        CreateMap<CreateCategoryTypeCategoriesDto, Domain.CategoryTypeCategories>();
        CreateMap<UpdateCategoryTypeCategoriesDto, Domain.CategoryTypeCategories>();

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
