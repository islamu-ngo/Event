using Explore.Application.DTOs.GroupMember;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.GroupMembers.Requests.Queries;

public sealed record GetGroupMembersRequest(Guid GroupId = default) : IQuery<List<GroupMemberDto>>;
