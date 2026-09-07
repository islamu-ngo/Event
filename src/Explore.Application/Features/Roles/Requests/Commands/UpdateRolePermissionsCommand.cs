using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Roles.Requests.Commands;

public sealed record UpdateRolePermissionsCommand : IRequest<BaseCommandResponse<int>>
{
    public int RoleId { get; init; }

    /// <summary>
    /// New complete set of permission IDs (replaces existing).
    /// </summary>
    private IReadOnlyList<int> _permissionIds = Array.AsReadOnly(Array.Empty<int>());

    public required IReadOnlyList<int> PermissionIds
    {
        get => _permissionIds;
        init => _permissionIds = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }

    /// <summary>
    /// The caller's role IDs (set by handler from context).
    /// </summary>
    private IReadOnlyList<int> _callerRoleIds = Array.AsReadOnly(Array.Empty<int>());

    public IReadOnlyList<int> CallerRoleIds
    {
        get => _callerRoleIds;
        init => _callerRoleIds = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }

    /// <summary>
    /// The caller's highest scope (set by handler from context).
    /// </summary>
    public Domain.Enums.RoleScopeEnum CallerHighestScope { get; init; }
}
