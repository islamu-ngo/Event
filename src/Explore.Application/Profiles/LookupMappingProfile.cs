using AutoMapper;
using Explore.Application.DTOs.ActorType;
using Explore.Application.DTOs.CategoryType;
using Explore.Application.DTOs.CategoryTypeCategories;
using Explore.Application.DTOs.DidCustodyType;
using Explore.Application.DTOs.EventFormat;
using Explore.Application.DTOs.EventSessionStatus;
using Explore.Application.DTOs.EventStatus;
using Explore.Application.DTOs.FileType;
using Explore.Application.DTOs.Language;
using Explore.Application.DTOs.Location;
using Explore.Application.DTOs.LocationRoom;
using Explore.Application.DTOs.Madhab;
using Explore.Application.DTOs.OrganizationPosition;
using Explore.Application.DTOs.Tag;
using Explore.Application.DTOs.TagType;
using Explore.Application.DTOs.TagTypeTags;
using Explore.Application.DTOs.VisibilityType;
using Explore.Application.Lookups;
using Explore.Domain;

namespace Explore.Application.Profiles;

public class LookupMappingProfile : Profile
{
    public LookupMappingProfile()
    {
        CreateMap<Location, LocationDto>()
            .ForMember(destination => destination.Latitude, options => options.MapFrom(source => ValidLatitude(source)))
            .ForMember(destination => destination.Longitude, options => options.MapFrom(source => ValidLongitude(source)));
        CreateMap<Location, LocationListDto>();

        CreateMap<LocationRoom, LocationRoomDto>()
            .ForMember(dest => dest.LocationFullName, opt => opt.MapFrom(src => src.Location != null ? src.Location.FullName : null));
        CreateMap<LocationRoom, LocationRoomListDto>();
        CreateMap<CreateLocationRoomDto, LocationRoom>();

        CreateMap<Tag, TagDto>().ReverseMap();
        CreateMap<Tag, TagListDto>();
        CreateMap<CreateTagDto, Tag>();

        CreateMap<Language, LanguageDto>().ReverseMap();
        CreateMap<Language, LanguageListDto>().ReverseMap();

        CreateMap<CategoryType, CategoryTypeDto>().ReverseMap();
        CreateMap<CategoryType, CategoryTypeListDto>().ReverseMap();

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

        CreateMap<TagType, TagTypeDto>().ReverseMap();
        CreateMap<TagType, TagTypeListDto>().ReverseMap();

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

        CreateMap<Madhab, MadhabDto>().ReverseMap();
        CreateMap<Madhab, MadhabListDto>().ReverseMap();

        CreateMap<Domain.EventStatus, EventStatusDto>().ReverseMap();
        CreateMap<Domain.EventStatus, EventStatusListDto>().ReverseMap();

        CreateMap<Domain.EventSessionStatus, EventSessionStatusDto>().ReverseMap();
        CreateMap<Domain.EventSessionStatus, EventSessionStatusListDto>().ReverseMap();

        CreateMap<EventFormat, EventFormatDto>().ReverseMap();
        CreateMap<EventFormat, EventFormatListDto>().ReverseMap();

        CreateMap<VisibilityType, VisibilityTypeDto>().ReverseMap();
        CreateMap<VisibilityType, VisibilityTypeListDto>().ReverseMap();

        CreateMap<Domain.ActorType, ActorTypeDto>().ReverseMap();
        CreateMap<Domain.ActorType, ActorTypeListDto>().ReverseMap();

        CreateMap<Domain.DidCustodyType, DidCustodyTypeDto>().ReverseMap();
        CreateMap<Domain.DidCustodyType, DidCustodyTypeListDto>().ReverseMap();

        CreateMap<Domain.OrganizationPosition, OrganizationPositionDto>().ReverseMap();
        CreateMap<Domain.OrganizationPosition, OrganizationPositionListDto>().ReverseMap();

        CreateMap<Domain.GroupPosition, DTOs.GroupPosition.GroupPositionDto>().ReverseMap();
        CreateMap<Domain.GroupPosition, DTOs.GroupPosition.GroupPositionListDto>().ReverseMap();

        CreateMap<Domain.Role, DTOs.Role.RoleDto>()
            .ForMember(dest => dest.RoleScopeCode, opt => opt.MapFrom(src => NormalizedLookupMetadata.RoleScope(src.RoleScopeId).Code))
            .ForMember(dest => dest.RoleScopeName, opt => opt.MapFrom(src => NormalizedLookupMetadata.RoleScope(src.RoleScopeId).Name));
        CreateMap<Domain.Role, DTOs.Role.RoleListDto>()
            .ForMember(dest => dest.RoleScopeCode, opt => opt.MapFrom(src => NormalizedLookupMetadata.RoleScope(src.RoleScopeId).Code))
            .ForMember(dest => dest.RoleScopeName, opt => opt.MapFrom(src => NormalizedLookupMetadata.RoleScope(src.RoleScopeId).Name));

        CreateMap<Domain.Permission, DTOs.Permission.PermissionDto>()
            .ForMember(dest => dest.RoleScopeCode, opt => opt.MapFrom(src => NormalizedLookupMetadata.RoleScope(src.RoleScopeId).Code))
            .ForMember(dest => dest.RoleScopeName, opt => opt.MapFrom(src => NormalizedLookupMetadata.RoleScope(src.RoleScopeId).Name));
        CreateMap<Domain.Permission, DTOs.Permission.PermissionListDto>()
            .ForMember(dest => dest.RoleScopeCode, opt => opt.MapFrom(src => NormalizedLookupMetadata.RoleScope(src.RoleScopeId).Code))
            .ForMember(dest => dest.RoleScopeName, opt => opt.MapFrom(src => NormalizedLookupMetadata.RoleScope(src.RoleScopeId).Name));

        CreateMap<Domain.FileType, FileTypeDto>().ReverseMap();
        CreateMap<Domain.FileType, FileTypeListDto>().ReverseMap();
    }

    private static double? ValidLatitude(Location location) => location.GetCoordinate()?.Latitude;

    private static double? ValidLongitude(Location location) => location.GetCoordinate()?.Longitude;
}
