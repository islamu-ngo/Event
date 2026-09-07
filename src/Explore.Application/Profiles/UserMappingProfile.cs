using AutoMapper;
using Explore.Application.DTOs.User;
using Explore.Application.DTOs.UserAuthenticationToken;
using Explore.Domain;

namespace Explore.Application.Profiles;

public class UserMappingProfile : Profile
{
    public UserMappingProfile()
    {
        CreateMap<User, UserDto>()
            .ForMember(dest => dest.ActorDisplayName, opt => opt.MapFrom(src => src.Actor != null ? src.Actor.DisplayName : null))
            .ForMember(dest => dest.ActorHandle, opt => opt.MapFrom(src => src.Actor != null ? src.Actor.AtprotoIdentities.Select(identity => identity.Handle).FirstOrDefault() : null))
            .ForMember(dest => dest.ActorBackgroundColor, opt => opt.MapFrom(src => src.Actor != null ? src.Actor.BackgroundColor : null))
            .ForMember(dest => dest.ActorBackgroundEffect, opt => opt.MapFrom(src => src.Actor != null ? src.Actor.BackgroundEffect : null))
            .ForMember(dest => dest.ActorBannerColor, opt => opt.MapFrom(src => src.Actor != null ? src.Actor.BannerColor : null))
            .ForMember(dest => dest.ActorBannerPictureId, opt => opt.Ignore())
            .ForMember(dest => dest.ActorBannerPictureUri, opt => opt.Ignore())
            .ForMember(dest => dest.ActorBackgroundImageId, opt => opt.Ignore())
            .ForMember(dest => dest.ActorBackgroundImageUri, opt => opt.Ignore());
        CreateMap<UpdateUserDto, User>();
        CreateMap<UpdateUserNamesDto, User>();

        CreateMap<Domain.UserAuthenticationToken, UserAuthenticationTokenDto>();
        CreateMap<Domain.UserAuthenticationToken, UserAuthenticationTokenListDto>();

    }
}
