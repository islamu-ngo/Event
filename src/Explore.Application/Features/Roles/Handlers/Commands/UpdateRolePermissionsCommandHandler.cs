using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Roles.Requests.Commands;
using Explore.Application.Responses;

namespace Explore.Application.Features.Roles.Handlers.Commands;

public class UpdateRolePermissionsCommandHandler : ICommandHandler<UpdateRolePermissionsCommand, BaseCommandResponse<int>>
{
    private readonly IRoleRepository _roleRepository;
    private readonly ICapabilityCeilingService _capabilityCeiling;
    private readonly IPermissionRegistryService _permissionRegistry;

    public UpdateRolePermissionsCommandHandler(
        IRoleRepository roleRepository,
        ICapabilityCeilingService capabilityCeiling,
        IPermissionRegistryService permissionRegistry)
    {
        _roleRepository = roleRepository;
        _capabilityCeiling = capabilityCeiling;
        _permissionRegistry = permissionRegistry;
    }

    public async Task<BaseCommandResponse<int>> ExecuteAsync(UpdateRolePermissionsCommand request, CancellationToken cancellationToken)
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

        // Capability ceiling validation
        var ceilingErrors = await _capabilityCeiling.ValidatePermissionAssignmentAsync(
            request.CallerRoleIds,
            request.CallerHighestScope,
            request.PermissionIds,
            role.Scope);

        if (ceilingErrors.Count > 0)
        {
            return BaseCommandResponse.Validation<int>(
                ceilingErrors,
                "Permission assignment validation failed.");
        }

        // Replace all permissions atomically
        await _roleRepository.ReplacePermissionsAsync(request.RoleId, request.PermissionIds);

        // Invalidate permission cache
        _permissionRegistry.InvalidateCache();

        return BaseCommandResponse.Success(
            request.RoleId,
            $"Role '{role.FullName}' updated with {request.PermissionIds.Count} permissions.");
    }
}
