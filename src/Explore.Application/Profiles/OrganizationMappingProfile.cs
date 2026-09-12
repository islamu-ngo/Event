using AutoMapper;
using Explore.Application.DTOs.OrganizationReview;
using Explore.Application.DTOs.StatusType;
using Explore.Domain;

namespace Explore.Application.Profiles;

public class OrganizationMappingProfile : Profile
{
    public OrganizationMappingProfile()
    {
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
