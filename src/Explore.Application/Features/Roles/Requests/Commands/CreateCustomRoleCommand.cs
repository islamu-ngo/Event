using Explore.Application.Responses;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.Roles.Requests.Commands;

public sealed record CreateCustomRoleCommand : IRequest<BaseCommandResponse<int>>
{
    public required string FullName { get; init; }
    public string? Description { get; init; }
    public RoleScopeEnum Scope { get; init; }

    /// <summary>
    /// Permission IDs to assign to this role.
    /// </summary>
    private IReadOnlyList<int> _permissionIds = Array.AsReadOnly(Array.Empty<int>());

    public required IReadOnlyList<int> PermissionIds
    {
        get => _permissionIds;
        init => _permissionIds = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }

    /// <summary>
    /// The caller's role IDs (set by handler from context, not from client).
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
    public RoleScopeEnum CallerHighestScope { get; init; }
}
