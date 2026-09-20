using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Role;

namespace Explore.Application.Features.Roles.Requests.Queries;

public sealed record GetRoleListRequest : IQuery<List<RoleListDto>>
{
    /// <summary>
    /// Optional normalized role scope lookup ID filter. When null, returns all roles.
    /// </summary>
    public int? RoleScopeId { get; init; }
}
