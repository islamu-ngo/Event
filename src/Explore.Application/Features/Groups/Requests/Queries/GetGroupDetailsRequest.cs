using Explore.Application.DTOs.Group;
using MediatR;

namespace Explore.Application.Features.Groups.Requests.Queries;

public sealed record GetGroupDetailsRequest(Guid Id = default) : IRequest<GroupDto>;
