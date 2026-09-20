using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.GroupMember;
using Explore.Application.Features.GroupMembers.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.GroupMembers.Handlers.Queries;

public class GetGroupMembersRequestHandler : IQueryHandler<GetGroupMembersRequest, List<GroupMemberDto>>
{
    private readonly IGroupMemberRepository _groupMemberRepository;

    public GetGroupMembersRequestHandler(IGroupMemberRepository groupMemberRepository)
    {
        _groupMemberRepository = groupMemberRepository;
    }

    public async Task<List<GroupMemberDto>> QueryAsync(GetGroupMembersRequest request, CancellationToken cancellationToken)
    {
        var members = await _groupMemberRepository.GetMembersByGroupId(request.GroupId);
        return members.Select(OrganizationMapper.ToGroupMember).ToList();
    }
}
