using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.GroupMember;
using Explore.Application.Features.GroupMembers.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.GroupMembers.Handlers.Queries;

public class GetGroupMemberDetailsRequestHandler : IRequestHandler<GetGroupMemberDetailsRequest, GroupMemberDto?>
{
    private readonly IGroupMemberRepository _groupMemberRepository;
    private readonly IMapper _mapper;

    public GetGroupMemberDetailsRequestHandler(IGroupMemberRepository groupMemberRepository, IMapper mapper)
    {
        _groupMemberRepository = groupMemberRepository;
        _mapper = mapper;
    }

    public async Task<GroupMemberDto?> Handle(GetGroupMemberDetailsRequest request, CancellationToken cancellationToken)
    {
        var member = await _groupMemberRepository.GetGroupMemberWithDetails(request.Id);
        if (member is null) return null;
        return _mapper.Map<GroupMemberDto>(member);
    }
}
