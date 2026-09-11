using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.GroupMember;
using Explore.Application.Features.GroupMembers.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.GroupMembers.Handlers.Queries;

public class GetGroupMembersRequestHandler : IRequestHandler<GetGroupMembersRequest, List<GroupMemberDto>>
{
    private readonly IGroupMemberRepository _groupMemberRepository;

    public GetGroupMembersRequestHandler(IGroupMemberRepository groupMemberRepository)
    {
        _groupMemberRepository = groupMemberRepository;
    }

    public async Task<List<GroupMemberDto>> Handle(GetGroupMembersRequest request, CancellationToken cancellationToken)
    {
        var members = await _groupMemberRepository.GetMembersByGroupId(request.GroupId);
        return members.Select(OrganizationMapper.ToGroupMember).ToList();
    }
}
