using Explore.Application.DTOs.Group;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Groups.Requests.Queries;

public sealed record GetGroupDetailsRequest(Guid Id = default) : IQuery<GroupDto?>;
