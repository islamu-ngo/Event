using AutoMapper;
using Explore.Application.DTOs.TenantUserRoleGrant;
using Explore.Domain;

namespace Explore.Application.Profiles;

public class TenantMappingProfile : Profile
{
    public TenantMappingProfile()
    {
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

    }
}
