using AutoMapper;
using Explore.Application.DTOs.StatusType;
using Explore.Domain;

namespace Explore.Application.Profiles;

public class OrganizationMappingProfile : Profile
{
    public OrganizationMappingProfile()
    {
        // Approval Status
        CreateMap<ApprovalStatus, StatusTypeListDto>().ReverseMap();

    }
}
