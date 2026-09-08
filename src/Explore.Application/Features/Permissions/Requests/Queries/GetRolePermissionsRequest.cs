using Explore.Application.DTOs.Permission;
using MediatR;

namespace Explore.Application.Features.Permissions.Requests.Queries;

public sealed record GetRolePermissionsRequest(int RoleId = default) : IRequest<List<RolePermissionDto>>;
