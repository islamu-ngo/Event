using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.OrganizationMember;
using Explore.Application.Features.OrganizationMembers.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.OrganizationMembers.Handlers.Queries;

public class GetMyInvitationsRequestHandler : IRequestHandler<GetMyInvitationsRequest, List<OrganizationInvitationDto>>
{
    private readonly IOrganizationMemberRepository _organizationMemberRepository;

    public GetMyInvitationsRequestHandler(IOrganizationMemberRepository organizationMemberRepository)
    {
        _organizationMemberRepository = organizationMemberRepository;
    }

    public async Task<List<OrganizationInvitationDto>> Handle(GetMyInvitationsRequest request, CancellationToken cancellationToken)
    {
        var invitations = await _organizationMemberRepository.GetInvitesByEmail(request.Email);
        return invitations.Select(OrganizationMapper.ToOrganizationInvitation).ToList();
    }
}
