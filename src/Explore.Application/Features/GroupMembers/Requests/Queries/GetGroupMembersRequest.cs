using Explore.Application.DTOs.GroupMember;
using MediatR;

namespace Explore.Application.Features.GroupMembers.Requests.Queries;

public sealed record GetGroupMembersRequest(Guid GroupId = default) : IRequest<List<GroupMemberDto>>;
