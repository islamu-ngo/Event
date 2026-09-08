using AutoMapper;
using Explore.Application.DTOs.Footer;
using Explore.Application.DTOs.Tenant;
using Explore.Application.DTOs.TenantUserRoleGrant;
using Explore.Domain;

namespace Explore.Application.Profiles;

public class TenantMappingProfile : Profile
{
    public TenantMappingProfile()
    {
        CreateMap<Tenant, TenantDto>().ReverseMap();
        CreateMap<Tenant, TenantListDto>().ReverseMap();
        CreateMap<CreateTenantDto, Tenant>();

        CreateMap<TenantNavigationLink, TenantNavigationLinkDto>();
        CreateMap<CreateTenantNavigationLinkDto, TenantNavigationLink>();

        CreateMap<TenantUserRoleGrant, TenantUserRoleGrantDto>()
            .ForMember(dest => dest.UserId, opt => opt.MapFrom(src => src.TenantUser.UserId))
            .ForMember(dest => dest.UserEmail, opt => opt.MapFrom(src => src.TenantUser.User != null ? src.TenantUser.User.Email : null))
            .ForMember(dest => dest.UserFullName, opt => opt.MapFrom(src => src.TenantUser.User != null ? $"{src.TenantUser.User.FirstName} {src.TenantUser.User.LastName}" : null))
            .ForMember(dest => dest.TenantFullName, opt => opt.MapFrom(src => src.Tenant != null ? src.Tenant.FullName : null))
            .ForMember(dest => dest.RoleName, opt => opt.MapFrom(src => src.Role != null ? src.Role.FullName : null));
        CreateMap<TenantUserRoleGrant, TenantUserRoleGrantListDto>()
            .ForMember(dest => dest.UserId, opt => opt.MapFrom(src => src.TenantUser.UserId))
            .ForMember(dest => dest.UserEmail, opt => opt.MapFrom(src => src.TenantUser.User != null ? src.TenantUser.User.Email : null))
            .ForMember(dest => dest.UserFullName, opt => opt.MapFrom(src => src.TenantUser.User != null ? $"{src.TenantUser.User.FirstName} {src.TenantUser.User.LastName}" : null))
            .ForMember(dest => dest.TenantFullName, opt => opt.MapFrom(src => src.Tenant != null ? src.Tenant.FullName : null))
            .ForMember(dest => dest.RoleName, opt => opt.MapFrom(src => src.Role != null ? src.Role.FullName : null));
        CreateMap<CreateTenantUserRoleGrantDto, TenantUserRoleGrant>();

        CreateMap<TenantFooterLink, FooterLinkItemDto>();

        CreateMap<TenantFooterLinkGroup, FooterLinkGroupDto>()
            .ForMember(d => d.Links, opt => opt.MapFrom(s => s.Links));

        CreateMap<TenantFooterLinkGroup, FooterLinkGroupListDto>()
            .ForMember(d => d.LinkCount, opt => opt.MapFrom(s => s.Links.Count));

        CreateMap<TenantFooterLinkGroup, FooterLinkGroupDetailsDto>()
            .ForMember(d => d.Links, opt => opt.MapFrom(s => s.Links));
    }
}
