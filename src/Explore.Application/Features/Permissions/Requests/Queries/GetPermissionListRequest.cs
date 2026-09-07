using Explore.Application.DTOs.Permission;
using MediatR;

namespace Explore.Application.Features.Permissions.Requests.Queries;

public sealed record GetPermissionListRequest : IRequest<List<PermissionListDto>>
{
    /// <summary>
    /// Optional normalized role scope lookup ID filter.
    /// </summary>
    public int? RoleScopeId { get; init; }

    /// <summary>
    /// Optional group filter (e.g., "Events", "Organizations").
    /// </summary>
    public string? GroupName { get; init; }

    /// <summary>
    /// When true, hides IsFiltered permissions (dangerous ones like tenant:delete).
    /// Default true for non-super-admins.
    /// </summary>
    public bool ExcludeFiltered { get; init; } = true;
}
