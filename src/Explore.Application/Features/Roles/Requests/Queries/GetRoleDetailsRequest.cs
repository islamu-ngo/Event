using Explore.Application.DTOs.Role;
using MediatR;

namespace Explore.Application.Features.Roles.Requests.Queries;

public sealed record GetRoleDetailsRequest(int Id = default) : IRequest<RoleDto?>;
