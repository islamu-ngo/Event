using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Role;

namespace Explore.Application.Features.Roles.Requests.Queries;

public sealed record GetRoleDetailsRequest(int Id = default) : IQuery<RoleDto?>;
