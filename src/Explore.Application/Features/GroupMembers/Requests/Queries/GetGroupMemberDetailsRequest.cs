using System;
using Explore.Application.DTOs.GroupMember;
using MediatR;

namespace Explore.Application.Features.GroupMembers.Requests.Queries;

public sealed record GetGroupMemberDetailsRequest(Guid Id = default) : IRequest<GroupMemberDto?>;
