using AutoMapper;
using Explore.Application.DTOs.Actor;
using Explore.Application.DTOs.StorageObject;
using Explore.Domain;

namespace Explore.Application.Profiles;

public class ActorFederationMappingProfile : Profile
{
    public ActorFederationMappingProfile()
    {
        CreateMap<Domain.Actor, ActorDto>()
            .ForMember(dest => dest.ActorTypeMasterCode, opt => opt.MapFrom(src => src.ActorType != null ? src.ActorType.MasterCode : null))
            .ForMember(dest => dest.ActorTypeFullName, opt => opt.MapFrom(src => src.ActorType != null ? src.ActorType.FullName : null))
            .ForMember(dest => dest.Did, opt => opt.MapFrom(src => src.AtprotoIdentities.Select(identity => identity.Did).FirstOrDefault()))
            .ForMember(dest => dest.Handle, opt => opt.MapFrom(src => src.AtprotoIdentities.Select(identity => identity.Handle).FirstOrDefault()))
            .ForMember(dest => dest.PdsHost, opt => opt.MapFrom(src => src.AtprotoIdentities.Select(identity => identity.PdsHost).FirstOrDefault()))
            .ForMember(dest => dest.IndexedAt, opt => opt.MapFrom(src => src.AtprotoIdentities.Select(identity => (DateTime?)identity.LastResolvedAt).FirstOrDefault()))
            .ForMember(dest => dest.DidCustodyTypeId, opt => opt.Ignore())
            .ForMember(dest => dest.DidCustodyTypeMasterCode, opt => opt.Ignore())
            .ForMember(dest => dest.DidCustodyTypeFullName, opt => opt.Ignore())
            .ForMember(dest => dest.ProfilePictureId, opt => opt.Ignore())
            .ForMember(dest => dest.BannerPictureId, opt => opt.Ignore())
            .ForMember(dest => dest.BackgroundImageId, opt => opt.Ignore())
            .ForMember(dest => dest.BackgroundImageUri, opt => opt.Ignore());
        CreateMap<Domain.Actor, ActorListDto>()
            .ForMember(dest => dest.ActorTypeMasterCode, opt => opt.MapFrom(src => src.ActorType != null ? src.ActorType.MasterCode : null))
            .ForMember(dest => dest.ActorTypeFullName, opt => opt.MapFrom(src => src.ActorType != null ? src.ActorType.FullName : null))
            .ForMember(dest => dest.Did, opt => opt.MapFrom(src => src.AtprotoIdentities.Select(identity => identity.Did).FirstOrDefault()))
            .ForMember(dest => dest.Handle, opt => opt.MapFrom(src => src.AtprotoIdentities.Select(identity => identity.Handle).FirstOrDefault()))
            .ForMember(dest => dest.PdsHost, opt => opt.MapFrom(src => src.AtprotoIdentities.Select(identity => identity.PdsHost).FirstOrDefault()))
            .ForMember(dest => dest.IndexedAt, opt => opt.MapFrom(src => src.AtprotoIdentities.Select(identity => (DateTime?)identity.LastResolvedAt).FirstOrDefault()))
            .ForMember(dest => dest.DidCustodyTypeId, opt => opt.Ignore())
            .ForMember(dest => dest.DidCustodyTypeMasterCode, opt => opt.Ignore())
            .ForMember(dest => dest.DidCustodyTypeFullName, opt => opt.Ignore())
            .ForMember(dest => dest.ProfilePictureId, opt => opt.Ignore())
            .ForMember(dest => dest.BannerPictureId, opt => opt.Ignore())
            .ForMember(dest => dest.BackgroundImageId, opt => opt.Ignore())
            .ForMember(dest => dest.BackgroundImageUri, opt => opt.Ignore());
        CreateMap<CreateActorDto, Domain.Actor>();

        CreateMap<Domain.StorageObject, StorageObjectDto>()
            .ForMember(dest => dest.FileTypeFullName, opt => opt.MapFrom(src => src.FileType != null ? src.FileType.FullName : null))
            .ForMember(dest => dest.TenantFullName, opt => opt.MapFrom(src => src.Tenant != null ? src.Tenant.FullName : null))
            .ForMember(dest => dest.ActorDisplayName, opt => opt.MapFrom(src => src.Actor != null ? src.Actor.DisplayName : null));
        CreateMap<Domain.StorageObject, StorageObjectListDto>()
            .ForMember(dest => dest.FileTypeFullName, opt => opt.MapFrom(src => src.FileType != null ? src.FileType.FullName : null));
    }
}
