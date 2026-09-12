using AutoMapper;
using Explore.Application.DTOs.OrganizationMember;
using Explore.Application.DTOs.OrganizationReview;
using Explore.Application.DTOs.StatusType;
using Explore.Domain;

namespace Explore.Application.Profiles;

public class OrganizationMappingProfile : Profile
{
    public OrganizationMappingProfile()
    {
        // Organization Member
        CreateMap<OrganizationMember, OrganizationMemberDto>()
            .ForMember(dest => dest.OrganizationFullName, opt => opt.MapFrom(src => src.OrganizationTenant.Organization.FullName))
            .ForMember(dest => dest.UserEmail, opt => opt.MapFrom(src => src.User != null ? src.User.Email : null))
            .ForMember(dest => dest.UserFullName, opt => opt.MapFrom(src => src.User != null ? $"{src.User.FirstName} {src.User.LastName}" : null))
            .ForMember(dest => dest.RoleName, opt => opt.MapFrom(src => src.Role != null ? src.Role.FullName : null))
            .ForMember(dest => dest.OrganizationPositionFullName, opt => opt.MapFrom(src => src.OrganizationPosition != null ? src.OrganizationPosition.FullName : null));
        CreateMap<AddOrganizationMemberDto, OrganizationMember>();
        CreateMap<UpdateOrganizationMemberRoleDto, OrganizationMember>();

        CreateMap<OrganizationMember, OrganizationInvitationDto>()
            .ForMember(dest => dest.OrganizationId, opt => opt.MapFrom(src => src.OrganizationTenant.OrganizationId))
            .ForMember(dest => dest.OrganizationName, opt => opt.MapFrom(src => src.OrganizationTenant.Organization.FullName))
            .ForMember(dest => dest.Role, opt => opt.MapFrom(src => (Explore.Domain.Enums.RoleEnum)src.RoleId))
            .ForMember(dest => dest.Email, opt => opt.MapFrom(src => src.User != null ? src.User.Email : null));

        // Approval Status
        CreateMap<ApprovalStatus, StatusTypeListDto>().ReverseMap();

        // Organization Review
        CreateMap<OrganizationReview, OrganizationReviewDto>()
            .ForMember(dest => dest.OrganizationFullName, opt => opt.MapFrom(src => src.Organization != null ? src.Organization.FullName : null))
            .ForMember(dest => dest.UserFullName, opt => opt.MapFrom(src => src.User != null ? $"{src.User.FirstName} {src.User.LastName}" : null));
        CreateMap<CreateOrganizationReviewDto, OrganizationReview>()
            .ForMember(dest => dest.EventId, opt => opt.MapFrom(src => src.ProgramId));
    }
}
