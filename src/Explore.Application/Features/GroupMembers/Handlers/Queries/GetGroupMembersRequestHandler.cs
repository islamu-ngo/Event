using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.GroupMember;
using Explore.Application.Features.GroupMembers.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.GroupMembers.Handlers.Queries;

public class GetGroupMembersRequestHandler : IRequestHandler<GetGroupMembersRequest, List<GroupMemberDto>>
{
    private readonly IGroupMemberRepository _groupMemberRepository;
    private readonly IMapper _mapper;

    public GetGroupMembersRequestHandler(IGroupMemberRepository groupMemberRepository, IMapper mapper)
    {
        _groupMemberRepository = groupMemberRepository;
        _mapper = mapper;
    }

    public async Task<List<GroupMemberDto>> Handle(GetGroupMembersRequest request, CancellationToken cancellationToken)
    {
        var members = await _groupMemberRepository.GetMembersByGroupId(request.GroupId);
        return _mapper.Map<List<GroupMemberDto>>(members);
    }
}
