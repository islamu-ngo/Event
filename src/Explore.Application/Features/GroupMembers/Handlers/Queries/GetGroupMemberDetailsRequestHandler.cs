using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.GroupMember;
using Explore.Application.Features.GroupMembers.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.GroupMembers.Handlers.Queries;

public class GetGroupMemberDetailsRequestHandler : IRequestHandler<GetGroupMemberDetailsRequest, GroupMemberDto?>
{
    private readonly IGroupMemberRepository _groupMemberRepository;

    public GetGroupMemberDetailsRequestHandler(IGroupMemberRepository groupMemberRepository)
    {
        _groupMemberRepository = groupMemberRepository;
    }

    public async Task<GroupMemberDto?> Handle(GetGroupMemberDetailsRequest request, CancellationToken cancellationToken)
    {
        var member = await _groupMemberRepository.GetGroupMemberWithDetails(request.Id);
        if (member is null) return null;
        return OrganizationMapper.ToGroupMember(member);
    }
}
