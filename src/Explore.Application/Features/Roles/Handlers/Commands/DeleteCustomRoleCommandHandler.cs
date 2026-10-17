using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Roles.Requests.Commands;
using Explore.Application.Responses;

namespace Explore.Application.Features.Roles.Handlers.Commands;

public class DeleteCustomRoleCommandHandler : ICommandHandler<DeleteCustomRoleCommand, BaseCommandResponse<int>>
{
    private readonly IRoleRepository _roleRepository;
    private readonly ICapabilityCeilingService _capabilityCeiling;
    private readonly IPermissionRegistryService _permissionRegistry;

    public DeleteCustomRoleCommandHandler(
        IRoleRepository roleRepository,
        ICapabilityCeilingService capabilityCeiling,
        IPermissionRegistryService permissionRegistry)
    {
        _roleRepository = roleRepository;
        _capabilityCeiling = capabilityCeiling;
        _permissionRegistry = permissionRegistry;
    }

    public async Task<BaseCommandResponse<int>> ExecuteAsync(DeleteCustomRoleCommand request, CancellationToken cancellationToken)
    {
        // Rule 4: System immutability check
        var modCheck = await _capabilityCeiling.CanModifyRoleAsync(request.RoleId);
        if (!modCheck.IsAllowed)
        {
            return BaseCommandResponse.Validation<int>([modCheck.Error!], modCheck.Error);
        }

        var role = await _roleRepository.GetByIdAsync(request.RoleId);
        if (role == null)
        {
            return BaseCommandResponse.Validation<int>(["Role not found."], "Role not found.");
        }

        // Check for active members
        var hasMembers = await _roleRepository.HasActiveMembersAsync(request.RoleId);
        if (hasMembers)
        {
            string message = $"Cannot delete role '{role.FullName}'. It still has active members assigned.";
            return BaseCommandResponse.Validation<int>([message], message);
        }

        // Remove all permissions first (RolePermission entries)
        await _roleRepository.RemoveAllPermissionsAsync(request.RoleId);

        await _roleRepository.Delete(role);

        // Invalidate permission cache
        _permissionRegistry.InvalidateCache();

        return BaseCommandResponse.Success(request.RoleId, $"Custom role '{role.FullName}' deleted.");
    }
}
