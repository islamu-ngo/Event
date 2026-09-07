using Explore.Application.DTOs.Role;
using MediatR;

namespace Explore.Application.Features.Roles.Requests.Queries;

public sealed record GetRoleListRequest : IRequest<List<RoleListDto>>
{
    /// <summary>
    /// Optional normalized role scope lookup ID filter. When null, returns all roles.
    /// </summary>
    public int? RoleScopeId { get; init; }
}
