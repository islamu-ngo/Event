using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.OrganizationMember;
using Explore.Application.Features.OrganizationMembers.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.OrganizationMembers.Handlers.Queries;

public class GetOrganizationMembersRequestHandler : IQueryHandler<GetOrganizationMembersRequest, List<OrganizationMemberDto>>
{
    private readonly IOrganizationMemberRepository _organizationMemberRepository;

    public GetOrganizationMembersRequestHandler(IOrganizationMemberRepository organizationMemberRepository)
    {
        _organizationMemberRepository = organizationMemberRepository;
    }

    public async Task<List<OrganizationMemberDto>> QueryAsync(GetOrganizationMembersRequest request, CancellationToken cancellationToken)
    {
        var members = await _organizationMemberRepository.GetMembersByOrganizationId(request.OrganizationId);
        return members.Select(OrganizationMapper.ToOrganizationMember).ToList();
    }
}
