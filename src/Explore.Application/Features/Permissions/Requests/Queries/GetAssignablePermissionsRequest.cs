using Explore.Application.DTOs.Permission;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.Permissions.Requests.Queries;

public sealed record GetAssignablePermissionsRequest : IRequest<List<PermissionListDto>>
{
    /// <summary>
    /// The caller's role IDs (from their memberships).
    /// </summary>
    private IReadOnlyList<int> _callerRoleIds = Array.AsReadOnly(Array.Empty<int>());

    public required IReadOnlyList<int> CallerRoleIds
    {
        get => _callerRoleIds;
        init => _callerRoleIds = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }

    /// <summary>
    /// The scope of the role being created/edited.
    /// </summary>
    public RoleScopeEnum TargetScope { get; init; }
}
