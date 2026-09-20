using System;
using Explore.Application.DTOs.GroupMember;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.GroupMembers.Requests.Queries;

public sealed record GetGroupMemberDetailsRequest(Guid Id = default) : IQuery<GroupMemberDto?>;
