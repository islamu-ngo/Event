using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Permission;

namespace Explore.Application.Features.Permissions.Requests.Queries;

public sealed record GetRolePermissionsRequest(int RoleId = default) : IQuery<List<RolePermissionDto>>;
