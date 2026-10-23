using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Roles.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;

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

        Role? role;
        try
        {
            role = await _roleRepository.DeleteUnusedCustomRoleAsync(request.RoleId, cancellationToken);
        }
        catch (BadRequestException exception)
        {
            return BaseCommandResponse.Validation<int>([exception.Message], exception.Message);
        }
        if (role is null)
            return BaseCommandResponse.Validation<int>(["Role not found."], "Role not found.");

        // Invalidate permission cache
        _permissionRegistry.InvalidateCache();

        return BaseCommandResponse.Success(request.RoleId, $"Custom role '{role.FullName}' deleted.");
    }
}
